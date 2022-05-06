#define WEBCAM

using UnityEngine;
using Unity.Barracuda;

#if WEBCAM
using System;
using System.Linq;

#if UNITY_WSA
using UnityEngine.Windows.WebCam;
#endif
#endif

public class Inference : MonoBehaviour
{
    private Model m_RuntimeModel;
    private IWorker m_Worker;
#if (WEBCAM)
    private WebCamTexture m_WebcamTexture;
#else
    private Tensor m_Input;
    public Texture2D inputImage;
#endif

    public NNModel inputModel;
    public Material preprocessMaterial;
    public Material postprocessMaterial;

    public int inputResolutionY = 32;
    public int inputResolutionX = 32;

#if (WEBCAM) && UNITY_WSA
    UnityEngine.Windows.WebCam.VideoCapture m_VideoCapture = null;
#endif

    void Start()
    {
        Application.targetFrameRate = 60;
		
        m_RuntimeModel = ModelLoader.Load(inputModel, false);
        m_Worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, m_RuntimeModel, false);

#if (WEBCAM)
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
        m_WebcamTexture = new WebCamTexture();
        m_WebcamTexture.Play();
#endif // !(UNITY_WSA)
#else
        //Setting texture for previsualizing input
        preprocessMaterial.mainTexture = inputImage;

        //Creating a rendertexture for the output render
        var targetRT = RenderTexture.GetTemporary(inputResolutionX, inputResolutionY, 0);
        Graphics.Blit(inputImage, targetRT, postprocessMaterial);
        m_Input = new Tensor(targetRT, 3);
        
        //m_Input = new Tensor(1, inputResolutionY, inputResolutionX, 3);
#endif //(WEBCAM)
    }

#if (WEBCAM)
    private void OnStartedVideoCaptureMode(VideoCapture.VideoCaptureResult result)
    {
        throw new NotImplementedException();
    }
#endif

    void Update()
    {
#if (WEBCAM)
        var targetRT = RenderTexture.GetTemporary(inputResolutionX, inputResolutionY, 0);
        Graphics.Blit(m_WebcamTexture, targetRT, postprocessMaterial);

        Tensor input = new Tensor(targetRT, 3);
#else
        Tensor input = m_Input;
#endif //!(WEBCAM)
        m_Worker.Execute(input);
        Tensor result = m_Worker.PeekOutput("output");
        
        RenderTexture resultMask = new RenderTexture(inputResolutionX, inputResolutionY, 0);
        resultMask.enableRandomWrite = true;
        resultMask.Create();
        
        result.ToRenderTexture(resultMask);

        postprocessMaterial.mainTexture = resultMask;
#if (WEBCAM)
        preprocessMaterial.mainTexture = targetRT;
#endif
    }
}
