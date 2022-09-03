using System;
using UnityEngine;

public static class Cropping 
{
    public static Texture2D Crop(this Texture2D tex, int width, int height)
    {
        Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, 0, false);
        result.SetPixels(tex.GetPixels(0, 0, width, height));
        result.Apply();
        return result;
    }
}
