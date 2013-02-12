using UnityEngine;

public static class Extensions {

    public static Vector3 RandomVectorNormalized()
    {
        return Random.insideUnitSphere.normalized;
    }

    public static Color ToColor(this Vector3 v)
    {
        return new Color(v.x, v.y, v.z);
    }

    public static bool Odd(this int x)
    {
        return x % 2 != 0;
    }
}
