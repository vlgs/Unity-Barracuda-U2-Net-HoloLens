using System;
using UnityEngine;

public abstract class VideoSource : MonoBehaviour, IDisposable
{
    internal abstract void Init();

    internal abstract Texture2D GrabFrame();

    public abstract void Dispose();
}
