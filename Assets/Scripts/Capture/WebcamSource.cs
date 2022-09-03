using UnityEngine;

public class WebcamSource : VideoSource
{
    private WebCamTexture m_WebcamTexture;

    internal override void Init()
    {
        m_WebcamTexture = new WebCamTexture();
        m_WebcamTexture.Play();

        var renderer = GetComponent<MeshRenderer>();
        if(renderer) renderer.material.mainTexture = m_WebcamTexture;
    }

    internal override Texture2D GrabFrame()
    {
        int inputResolutionWidth = 416, inputResolutionHeight = 416;
        //int inputResolutionWidth = 1280, inputResolutionHeight = 720;

        float ratio = (float)m_WebcamTexture.height/(float)m_WebcamTexture.width;
        var targetRT = RenderTexture.GetTemporary(inputResolutionWidth, inputResolutionHeight, 0);
        Graphics.Blit(m_WebcamTexture, targetRT, new Vector2(ratio,1f), new Vector2(0f,0f));

        Texture2D result = new Texture2D(inputResolutionWidth, inputResolutionHeight, TextureFormat.RGBA32, 0, false);
        result.SetPixels(m_WebcamTexture.GetPixels(0, 0, inputResolutionWidth, inputResolutionHeight));
        result.Apply();

        return result;
    }

    public override void Dispose()
    {
        m_WebcamTexture.Stop();
        m_WebcamTexture = null;
    }

}
