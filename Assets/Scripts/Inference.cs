//#define WEBCAM

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Barracuda;
using System.Linq;
using System;
using UnityEngine.UIElements;

#if WEBCAM && UNITY_WSA //&& !UNITY_EDITOR
using UnityEngine.Windows.WebCam;
#endif

public class Inference : MonoBehaviour
{
    private Model m_RuntimeModel;
    private IWorker m_Worker;
#if (WEBCAM)
    private WebCamTexture m_WebcamTexture;
#else
    private Tensor m_Input;
    public Texture2D targetRTinputImage;
#endif


    public NNModel inputModel;
    public Material preprocessMaterial;
    public Material postprocessMaterial;

    public int inputResolutionY = 32;
    public int inputResolutionX = 32;

    [Range(0,1),SerializeField] float confidenceThreshold = 0.3f;


#if UNITY_WSA
    UnityEngine.Windows.WebCam.VideoCapture m_VideoCapture = null;
#endif

    void Start()
    {
        Application.targetFrameRate = 60;

        m_RuntimeModel = ModelLoader.Load(inputModel, false);
        m_Worker = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, m_RuntimeModel, false);

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
#endif

#else
        var offset = new Vector2(0f, 0f);
        var scale = new Vector2(1f, 1f);

        //to render texture for cropping
        var targetRT = RenderTexture.GetTemporary(inputResolutionX, inputResolutionY, 0);
        Graphics.Blit(targetRTinputImage, targetRT, scale, offset);//postprocessMaterial);

        //Without normalizing, could have been...
        //m_Input = new Tensor(targetRT, 3);

        //to texture (for normalizing)
        var oldRT = RenderTexture.active;
        RenderTexture.active = targetRT;
        Texture2D result = new Texture2D(inputResolutionX, inputResolutionY,TextureFormat.RGBA32, 0, false);
        result.ReadPixels(new Rect(0, 0, inputResolutionX, inputResolutionX), 0, 0);
        result.Apply();

        //Normalize
        Tensor input = TransformInput(result.GetPixels32(), inputResolutionX, inputResolutionY);

        //to tensor
        m_Input = input;
        //render in scene
        preprocessMaterial.mainTexture = result; //or targetRT

        RenderTexture.active = oldRT;
        
        /*
         * only good if no resize to do
        //resize
        Texture2D result = new Texture2D(inputResolutionX, inputResolutionY, TextureFormat.RGBA32, 0, false);
        result.SetPixels32(0, 0, inputResolutionX, inputResolutionY, targetRTinputImage.GetPixels32());
        result.Apply();

        //transfrom
        Tensor input = TransformInput(result.GetPixels32(), inputResolutionX, inputResolutionY);

        //show in scene
        preprocessMaterial.mainTexture = result;

        //set as input
        m_Input = input;
        */
        
#endif
    }

#if UNITY_WSA
    private void OnStartedVideoCaptureMode(VideoCapture.VideoCaptureResult result)
    {
        throw new NotImplementedException();
    }
#endif

    private const int IMAGE_MEAN = 0;
    private const float IMAGE_STD = 1f;

    public static Tensor TransformInput(Color32[] pic, int width, int height)
    {
        float[] floatValues = new float[width * height * 3];

        for (int i = 0; i < pic.Length; ++i)
        {
            var color = pic[i];

            floatValues[i * 3 + 0] = (color.r - IMAGE_MEAN) / IMAGE_STD;
            floatValues[i * 3 + 1] = (color.g - IMAGE_MEAN) / IMAGE_STD;
            floatValues[i * 3 + 2] = (color.b - IMAGE_MEAN) / IMAGE_STD;
        }

        return new Tensor(1, height, width, 3, floatValues);
    }


    public static Color32[] TransformColor(Color32[] pic, int width, int height)
    {
        Color32[] output = new Color32[width * height * 3];

        for (int i = 0; i < pic.Length; ++i)
        {
            var color = pic[i];
            output[i] = new Color32(
                (byte)((color.r - IMAGE_MEAN) / IMAGE_STD * 255f),
                (byte)((color.g - IMAGE_MEAN) / IMAGE_STD * 255f),
                (byte)((color.b - IMAGE_MEAN) / IMAGE_STD * 255f),
                255
            );
        }

        return output;
    }


    void Update()
    {
#if (WEBCAM)
        // float ratio = (float)m_WebcamTexture.height/(float)m_WebcamTexture.width;
        // var targetRT = RenderTexture.GetTemporary(inputResolutionX, inputResolutionY, 0);
        // Graphics.Blit(m_WebcamTexture, targetRT, new Vector2(ratio,1f), new Vector2(0f,0f));

        Texture2D result = new Texture2D(inputResolutionX, inputResolutionY,TextureFormat.RGBA32, 0, false);
        result.SetPixels(m_WebcamTexture.GetPixels(0,0, inputResolutionX, inputResolutionY));
        result.Apply();
        preprocessMaterial.mainTexture = result;

        Tensor input = TransformInput(result.GetPixels32(), inputResolutionX, inputResolutionY);
#else
        Tensor input = m_Input;
#endif
        m_Worker.Execute(input);
        m_Worker.WaitForCompletion();

        //could have been, in a coroutine
        //yield return StartCoroutine(worker.StartManualSchedule(inputs));

        Tensor tensor = m_Worker.PeekOutput("grid");//"016_convolutional");

        List<YoloResult> results = new List<YoloResult>();

        for (int cx = 0; cx < ROW_COUNT; cx++)
        {
            for (int cy = 0; cy < COL_COUNT; cy++)
            {
                for (int b = 0; b < BOXES_PER_CELL; b++)
                {
                    var channel = (b * (CLASS_COUNT + BOX_INFO_FEATURE_COUNT));
                   
                    float tx = tensor[0, cx, cy, channel] ;
                    float ty = tensor[0, cx, cy, channel + 1];
                    float tw = tensor[0, cx, cy, channel + 2];
                    float th = tensor[0, cx, cy, channel + 3];
                    float tc = tensor[0, cx, cy, channel + 4];

                    float x = ((float)cy + Sigmoid(tx)) * CELL_WIDTH;
                    float y = ((float)cx + Sigmoid(ty)) * CELL_HEIGHT;

                    float w = Mathf.Exp(tw) * anchors[2*b    ] * CELL_WIDTH;
                    float h = Mathf.Exp(th) * anchors[2*b + 1] * CELL_HEIGHT;

                    float confidence = Sigmoid(tc);

                    if(confidence < confidenceThreshold)
                        continue;

                    var classes = new float[CLASS_COUNT];
                    var classOffset = channel + BOX_INFO_FEATURE_COUNT;

                    for (int c = 0; c < CLASS_COUNT; c++)
                    {
                        classes[c] = tensor[0, cx,cy, classOffset + c];
                    }

                    //softmax this
                    classes = Softmax(classes);

                    //argmax
                    float bestClassScore = classes.Max();
                    int bestClass = classes.ToList().IndexOf(bestClassScore);

                    float confidenceInClass = bestClassScore * confidence;

                    if (confidenceInClass > confidenceThreshold)
                    {
                        print($"{(Classes)bestClass} : {(bestClassScore*100).ToString("00")}% ({x};{y};{w};{h})");

                        results.Add(new YoloResult(){
                            x = x,
                            y = y,
                            width = w,
                            height = h,
                            confidence = confidence,
                            classes = classes,
                        });
                    }
                }
            }

        }

        print($"#### {results.Count}");

        /*
        RenderTexture resultMask = new RenderTexture(inputResolutionX, inputResolutionY, 0);
        resultMask.enableRandomWrite = true;
        resultMask.Create();

        result.ToRenderTexture(resultMask);
        */

        //postprocessMaterial.mainTexture = resultMask;
#if (WEBCAM)
        // preprocessMaterial.mainTexture = targetRT;
#endif
        //input.Dispose();
    }

    void OnDestroy()
    {
#if !(WEBCAM)
        m_Input.Dispose();
#endif
        m_Worker.Dispose();
    }

    private float Sigmoid(float value)
    {
        var k = (float)Math.Exp(value);
        return k / (1.0f + k);
    }

    private float[] Softmax(float[] values)
    {
        var maxVal = values.Max();
        var exp = values.Select(v => Math.Exp(v - maxVal));
        var sumExp = exp.Sum();

        return exp.Select(v => (float)(v / sumExp)).ToArray();
    }

    internal struct YoloResult{
        internal float x,y,width,height,confidence;
        internal float[] classes;
    }

    public const int ROW_COUNT = 13;
    public const int COL_COUNT = 13;
    public const int CHANNEL_COUNT = 125;
    public const int BOXES_PER_CELL = 5;
    public const int BOX_INFO_FEATURE_COUNT = 5;
    public const int CLASS_COUNT = 20;
    public const float CELL_WIDTH = 32;
    public const float CELL_HEIGHT = 32;
    private float[] anchors = new float[]{1.08f, 1.19f, 3.42f, 4.41f, 6.63f, 11.38f, 9.42f, 5.11f, 16.62f, 10.52f};

    enum Classes{
        aeroplane = 0,
        bicycle = 1,
        bird = 2,
        boat = 3,
        bottle = 4,
        bus = 5,
        car = 6,
        cat = 7,
        chair = 8,
        cow = 9,
        diningtable = 10,
        dog = 11,
        horse = 12,
        motorbike = 13,
        person = 14,
        pottedplant = 15,
        sheep = 16,
        sofa = 17,
        train = 18,
        tvmonitor = 19,
    }

}
