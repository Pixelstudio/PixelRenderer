using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

public class PixelRenderer : MonoBehaviour {
    public int NrPhotons = 2000;
    public int NrBounces = 3;

    private Light[] sceneLights;
    private Light[] SceneLights
    {
        get
        {
            if (sceneLights == null)
            {
                var allsceneLights = (Light[])FindObjectsOfType(typeof(Light));
                if (allsceneLights == null)
                    Debug.LogError("No lights found!");
                else
                    sceneLights = allsceneLights.Where(l => l.type == LightType.Point).ToArray();
                
            }
            return sceneLights;
        }
    }
    
    private const int SzImg             = 512;
    private const float MaxRayLength    = 5.9f;
    private const float SqRadius        = 0.7f;         // PhotonInformation Integration Area (Squared for Efficiency)
    private const float Exposure        = 10.0f;       // Number of PhotonsInformation Integrated at Brightest Pixel
  
    private bool            rayCastHit;
    private RaycastHit      hit;
    private Dictionary<GameObject, List<PhotonData>> photonData;
    private Color[]         colarray;
    private int             colArrayLength;
    private Texture2D       targetTexture;
    private int             pIteration = 0, pMax = 0, pCol = 0, pRow = 0;
    private DateTime           startTime;
	
    private void Start ()
    {
        photonData = new Dictionary<GameObject, List<PhotonData>>(); 
        colarray = new Color[SzImg * SzImg];
	    colArrayLength = SzImg*SzImg -1;
        targetTexture = new Texture2D(SzImg, SzImg);
        targetTexture.SetPixels(colarray);
        targetTexture.Apply();
        
        startTime = DateTime.Now;
        EmitPhotons();
        Debug.Log("Photon Emitting ::  time: " + (DateTime.Now - startTime).Milliseconds.ToString() + "ms");
        startTime = DateTime.Now;
        StartCoroutine(Render());
    }
   
    IEnumerator Render()
    {
        while (pMax <= SzImg)
        {
            var iterations = 0;
            while (iterations < ((Mathf.Max(pMax, 512))) && (pMax <= SzImg))
            {
                if (pCol >= pMax) //Render Pixels Out of Order With Increasing Resolution: 2x2, 4x4, 16x16... 512x512
                {
                    pRow++;
                    pCol = 0;
                    if (pRow >= pMax)
                    {
                        pIteration++;
                        pRow = 0;
                        pMax = (int)Mathf.Pow(2, pIteration);
                    }
                }
                var pNeedsDrawing = (pIteration == 1 || Odd(pRow) || (!Odd(pRow) && Odd(pCol)));
                var x = pCol * (SzImg / pMax);
                var y = pRow * (SzImg / pMax);
                pCol++;

                if (!pNeedsDrawing) continue;
                iterations++;
                colarray[colArrayLength - (y * SzImg + x)] = ComputePixelColor(SzImg - x, y);
            }
            yield return 0;
        }
        Debug.Log("Photon Rendering ::  time: " + (DateTime.Now - startTime).Seconds.ToString() + "sec");
        
        targetTexture.SetPixels(colarray);
        targetTexture.Apply();
    }
   
    private void OnGUI()
    {
        GUI.DrawTexture(new Rect(0,0,SzImg,SzImg),targetTexture );
    }
 
    private void RayTrace(Vector3 direction, Vector3 origin)
    {
        rayCastHit = Physics.Raycast(new Ray(origin, direction), out hit, MaxRayLength);
    }
   
    private void StorePhoton(GameObject target, Vector3 direction, Vector3 location, Color energy)
    {
        var photonInformation = new PhotonData { Direction = direction, Location = location, Energy= energy};
        
        List<PhotonData> pData;
        if (photonData.TryGetValue(target, out pData))
            pData.Add(photonInformation);
        else
            photonData.Add(target,new List<PhotonData>(){photonInformation});
        
    }

    private void EmitPhotons()
    {
        Random.seed = 0;

        for (var i = 0; i < NrPhotons; i++)
        {
            foreach (var sceneLight in SceneLights)
            {
                var rgb = new Color(1f, 1f, 1f);               	                //Initial PhotonInformation Color is White (color of the light is white)...
                var bounces = 1;
                var ray = Extensions.RandomVectorNormalized();  
                var prevPoint = sceneLight.transform.position;      		    //Emit From Point LightPosition Source
                RayTrace(ray, prevPoint);                          	            //Trace the light ray Path
                
                while (rayCastHit && bounces <= NrBounces)
                {
                    rgb = FilterColor(rgb) * 1.0f / Mathf.Sqrt(bounces);        //We git one hit, calc color of ir. filter light and multi bounce
                    var go = hit.collider.gameObject;
                    StorePhoton(go, ray, hit.point, rgb);
                    ShadowPhoton(ray);
                    ray = Vector3.Reflect(ray, hit.normal);
                    RayTrace(ray, hit.point);
                    bounces++;
                }
            }
        }
    }

    private Color GatherPhotons()
    {
        var energy = new Color(0,0,0);
        var source = hit.collider.gameObject;
        var pInfo = photonData[source];
        var p = hit.point;
        var n = hit.normal;
        foreach (var info in pInfo)
        {
            var p1 = info.Location;
            var gSqDist = 0f;
            if (!GatedSqDist3(p, p1, SqRadius, out gSqDist)) continue;
            var p2 = info.Direction;
            var weight = Mathf.Max(0.0f, -Vector3.Dot(n, p2));              //Single PhotonInformation Diffuse Lighting
            weight *= (1.0f - Mathf.Sqrt(gSqDist)) / Exposure;              //Weight by PhotonInformation-Point Distance
            var p3 = info.Energy*weight;
            
            energy = energy + p3;                                           //Add PhotonInformation's EnergyVector to Total
        }
        //energy.a = 1;  
        return energy;
    }
  
    private Color ComputePixelColor(int x, int y)
    {
        var ray = new Vector3((float)x / SzImg - 0.5f, -((float)y / SzImg - 0.5f), 1.0f);
        RayTrace(ray, new Vector3(0,0,0));
        return rayCastHit ? GatherPhotons() : Color.white;
    }

    private static bool GatedSqDist3(Vector3 a, Vector3 b, float sqradius, out float result)
    {                                   //Gated Squared Distance
        result = 0;
        var c = a.x - b.x;
        var d = c * c;                  //Are Within a Radius of a Point (and Most Are Not!)
        if (d > sqradius)
            return false; //Gate 1          - If this dimension alone is larger than
        c = a.y - b.y;                //         the search radius, no need to continue
        d += c * c;
        if (d > sqradius)
            return false; //Gate 2
        c = a.z - b.z;
        d += c * c;
        if (d > sqradius)
            return false; //Gate 3
        result = d;
        return true; //Store Squared Distance Itself in Global State
    }
    
    private static bool Odd(int x)
    {
        return x % 2 != 0;
    }

    private void ShadowPhoton(Vector3 ray)
    {
        var shadow = new Color(-0.25f, -0.25f, -0.25f);					//Shadow PhotonsInformation
        var bumpedPoint = (hit.point + (ray * 0.00001f));      	            //Start Just Beyond Last Intersection
        RaycastHit sHit;
        if (!Physics.Raycast(new Ray(bumpedPoint, ray), out sHit, float.MaxValue)) return;
        StorePhoton(sHit.collider.gameObject, ray,sHit.point, shadow);
    }
    
    private Color FilterColor(Color rgbIn) //e.g. White light Hits Red Wall, bounces back red only 
    {
        var hitColor = Color.white;
        var rndr = hit.collider.renderer;
        if (rndr != null)
        {
            if (rndr.sharedMaterial.mainTexture != null)
            {
                var tex = (Texture2D) rndr.sharedMaterial.mainTexture;
                var pixelUv = hit.textureCoord;
                hitColor = tex.GetPixel((int) pixelUv.x, (int) pixelUv.y);
            }
            else
            {
                hitColor = rndr.sharedMaterial.color;
            }
        }
        return new Color(Mathf.Min(hitColor.r, rgbIn.r), Mathf.Min(hitColor.g, rgbIn.g), Mathf.Min(hitColor.b, rgbIn.b),1);
    }

}
