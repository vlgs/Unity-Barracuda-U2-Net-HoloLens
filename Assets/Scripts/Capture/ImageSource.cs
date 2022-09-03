using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ImageSource : VideoSource
{
    [SerializeField] private Texture2D m_image;

    internal override void Init()
    {
        //left blank on purpose
    }

    internal override Texture2D GrabFrame()
    {
        return m_image;
    }

    public override void Dispose()
    {
        m_image = null;
    }
}
