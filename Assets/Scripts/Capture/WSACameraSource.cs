using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if WEBCAM && UNITY_WSA //&& !UNITY_EDITOR
using UnityEngine.Windows.WebCam;
#endif

public class WSACameraSource : VideoSource
{

#if UNITY_WSA
    UnityEngine.Windows.WebCam.VideoCapture m_VideoCapture = null;
#endif

    internal override void Init()
    {

#if UNITY_WSA
        Resolution cameraResolution = VideoCapture.SupportedResolutions.OrderByDescending((res) => res.width * res.height).First();
        Debug.Log(cameraResolution);

        float cameraFramerate = VideoCapture.GetSupportedFrameRatesForResolution(cameraResolution).OrderByDescending((fps) => fps).First();
        Debug.Log(cameraFramerate);

        VideoCapture.CreateAsync(false, delegate (VideoCapture videoCapture)
        {
            if (videoCapture != null)
            {
                m_VideoCapture = videoCapture;
                //Debug.Log("Created VideoCapture Instance!");

                CameraParameters cameraParameters = new CameraParameters();
                cameraParameters.hologramOpacity = 0.0f;
                cameraParameters.frameRate = cameraFramerate;
                cameraParameters.cameraResolutionWidth = cameraResolution.width;
                cameraParameters.cameraResolutionHeight = cameraResolution.height;
                cameraParameters.pixelFormat = CapturePixelFormat.BGRA32;

                m_VideoCapture.StartVideoModeAsync(cameraParameters,
                                                   VideoCapture.AudioState.ApplicationAndMicAudio,
                                                   OnStartedVideoCaptureMode);
            }
            else
            {
                Debug.LogError("Failed to create VideoCapture Instance!");
            }
        });
#else
        throw new System.NotImplementedException();
#endif
    }


#if UNITY_WSA
    private void OnStartedVideoCaptureMode(VideoCapture.VideoCaptureResult result)
    {
        throw new NotImplementedException();
    }
#endif

    internal override Texture2D GrabFrame()
    {
#if UNITY_WSA
        //left blank on purpose
#else
        throw new System.NotImplementedException();
#endif
    }

    public override void Dispose()
    {
        throw new System.NotImplementedException();
    }
}
