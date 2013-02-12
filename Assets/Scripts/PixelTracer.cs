using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class PixelTracer2 : MonoBehaviour
{
    private static Camera sceneCamera;
    private static Camera SceneCamera
    {
        get { return sceneCamera != null ? sceneCamera : (sceneCamera = Camera.main); }
    }



    private static Light[] sceneLights;
    private static Light[] SceneLights
    {
        get { return sceneLights != null ? sceneLights : (sceneLights = (Light[]) FindObjectsOfType(typeof (Light))); }
    }

    private const int SzImg = 512;
    private const int clrArrayLength = SzImg * SzImg - 1;
    private const int MAX_DEPTH = 8;                                              // max recursion for reflections
    private const int MAX_PASSES = 64;
    private const int RAYS_PER_PIXEL = 32;                                         // how many rays to shoot per pixel?
    private const float PI2 = Mathf.PI * 2;
    private int pMax, pCol, pRow, pIteration;

    private static Color[] targetTextureColors2;
    private static Color[] targetTextureColors;
    
    private Texture2D targetTexture;
    private Texture2D TargetTexture
    {
        get { return targetTexture != null ? targetTexture : (targetTexture = new Texture2D(SzImg, SzImg,TextureFormat.RGB24, false)); }
    }
    private Texture2D lineTexture;
    private Texture2D LineTexture
    {
        get
        {
            if (lineTexture != null) 
                return lineTexture;

            lineTexture = new Texture2D(1, 1, TextureFormat.RGB24, false);
            lineTexture.SetPixel(0,0,Color.white);
            lineTexture.Apply();
            return lineTexture;
        }
    }
    
    private bool updateTexture;
    private string progress;
    private int x, y;
    
    void Start()
    {
        targetTextureColors = new Color[SzImg * SzImg];
        targetTextureColors2 = new Color[SzImg * SzImg];

        StartCoroutine(Render());    
    }
    
    IEnumerator Render()
    {

        for (var i = 1; i <= MAX_PASSES; i++)
        {
            for (y = 0; y < SzImg; y++)
            {
                for (x = 0; x < SzImg; x++)
                {
                    RenderPixel(SzImg - x, y);
                    targetTextureColors[clrArrayLength - (y * SzImg + x)] = targetTextureColors2[clrArrayLength - (y * SzImg + x)] / (RAYS_PER_PIXEL * i);
                }
                progress = "Pass : " + i + " Percent :" + Mathf.RoundToInt((100f / SzImg * y)) + " rays per pixel :" + i * RAYS_PER_PIXEL;
                yield return 0;
                updateTexture = true;
               
            }
            yield return 0;
        }
    }

    void Update ()
    {
        if (!updateTexture) return;
        TargetTexture.SetPixels(targetTextureColors);
        TargetTexture.Apply();
        updateTexture = false;
    }

    void OnGUI()
    {
        GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height), TargetTexture );
        GUI.DrawTexture(new Rect(0,y,Screen.width, 1), LineTexture);
        GUI.color = Color.red;
        GUI.Label(new Rect(0,0,Screen.width,20), "Progress :" + progress );
    }

    static void RenderPixel(int x, int y)
    {
        
        var pos = new Vector3((float)x / SzImg - 0.5f, -((float)y / SzImg - 0.5f), 1.0f);
        var ray = new Ray(SceneCamera.transform.position, pos);
  
        float r = 0, g = 0, b = 0;

        for (var i = 0; i < RAYS_PER_PIXEL; i++)
        {
            var c = Trace(ray, 1);
            r += c.r;
            g += c.g;
            b += c.b;
        }
        targetTextureColors2[clrArrayLength - (y*SzImg + 512-x)] += new Color(r, g, b, 1);
        //return new Color(r, g, b ,1) ;
    }

    static Color Trace(Ray ray, int traceDepth)
    {
        if (traceDepth >= MAX_DEPTH)
            return Color.black;
        
        // See if the ray intersected an object (only if it hasn't already got one - we don't need to
        // recalculate the first intersection for each sample on the same pixel!)
        RaycastHit hit;
        if (!Physics.Raycast(ray, out hit, float.MaxValue))
        {
            return Color.black;
        }

        var tag = hit.collider.tag;
        var surfaceNormal = hit.normal;
        var reflectionDirection = Vector3.zero;

        switch (tag)
        {
            case "Light":
                return Color.white;
                break;
            case "Reflective":
                reflectionDirection = ray.direction - (surfaceNormal * (2 * Vector3.Dot(surfaceNormal, ray.direction)));
                break;
            case "Refractive":
                {
                    var forward = (Vector3.Dot(surfaceNormal, ray.direction) < 0) ? surfaceNormal : surfaceNormal * -1.0f;
                    var reflected = ray.direction - (surfaceNormal * (2 * Vector3.Dot(surfaceNormal, ray.direction)));
                    var entering = Vector3.Dot(surfaceNormal, forward) > 0; // normal.dot(forward) > 0; // ray from outside going in ?
                    var air = 1f;
                    var glass = 1.5f;
                    var refraction = entering ? air / glass : glass / air;
                    var angle = Vector3.Dot(ray.direction, forward);// ray.direction.dot(forward);
                    var cos2t = 1 - refraction * refraction * (1 - angle * angle);
                    reflectionDirection = reflected;
                    if (cos2t > 0)
                    {
                        var tdir = (ray.direction*refraction) -
                                   (surfaceNormal * (entering ? +1 : -1) * (angle * refraction + Mathf.Sqrt(cos2t))).normalized;
                        var glassMair = glass - air;
                        var glassPair = glass + air;
                        var c = 1f - (entering ? -angle : Vector3.Dot(tdir, surfaceNormal));
                        var R0 = glassMair * glassMair / (glassPair * glassPair);
                        var Re = R0 + (1f - R0)*c*c*c*c*c;
                        var Tr = 1f - Re;
                        var P = .25f + 0.5f*Re;
                        var RP = Re/P;
                        var TP = Tr/(1 - P);
                        traceDepth++;
                        if (traceDepth <= 2)
                            reflectionDirection = tdir*Tr;
                        else
                        {
                            if (Random.value < P)
                                reflectionDirection = reflected*RP;
                            else
                                reflectionDirection = tdir*TP;
                        }
                    }

                }
                break;
            default:
                {
                    if (Vector3.Dot(surfaceNormal, ray.direction) >= 0) surfaceNormal = surfaceNormal*-1.0f;
                    var r1 = Random.value*PI2;
                    var r2 = Random.value;
                    var r2S = Mathf.Sqrt(r2);
                    var u = (Mathf.Abs(surfaceNormal.x) > 0.1f) ? Vector3.up : Vector3.forward;
                    u = Vector3.Cross(u, surfaceNormal).normalized;
                    var v = Vector3.Cross(u, surfaceNormal);
                    reflectionDirection = (u * Mathf.Cos(r1) * r2S + v * Mathf.Sin(r1) * r2S + surfaceNormal * Mathf.Sqrt(1 - r2));
                    reflectionDirection.Normalize();
                }
                break;
        }
        // Now set up a direction from the hitpoint to that chosen point
        // And follow that path (note that we're not spawning a new ray -- just following the one we were
        // originally passed for MAX_DEPTH jumps)
        var reflectionRay = new Ray(hit.point, reflectionDirection);
        var reflectionCol = Trace(reflectionRay, traceDepth + 1);

        // Now factor the colour we got from the reflection
        // into this object's own colour; ie, illuminate
        // the current object with the results of that reflection

        var clr = HitColor(hit);
        var r = clr.r * reflectionCol.r;
        var g = clr.g * reflectionCol.g;
        var b = clr.b * reflectionCol.b;

        return new Color( r, g, b, 1);
    }

    static readonly Dictionary<Collider,Color> Hitcache = new Dictionary<Collider, Color>();

    private static Color HitColor(RaycastHit hit)
    {
        Color result;
        if (Hitcache.TryGetValue(hit.collider, out result))
            return result;

        var rndr = hit.collider.renderer;
        if (rndr != null)
        {
            if (rndr.sharedMaterial.mainTexture != null)
            {
                var tex = (Texture2D) rndr.sharedMaterial.mainTexture;
                var pixelUv = hit.textureCoord;
                result = tex.GetPixel((int) pixelUv.x, (int) pixelUv.y);
            }
            result = rndr.sharedMaterial.color;
        }
        
        Hitcache.Add(hit.collider,result);
        return result;
    }
}
