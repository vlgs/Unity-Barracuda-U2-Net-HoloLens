#define WEBCAM

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Barracuda;
using System.Linq;
using System;

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
    public Texture2D inputImage;
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
#endif

#else
        var targetRT = RenderTexture.GetTemporary(inputResolutionX, inputResolutionY, 0);

        float ratio = inputImage.height/inputImage.width;

        Graphics.Blit(inputImage, targetRT, new Vector2(ratio,1f), new Vector2(0.3f,0f));//postprocessMaterial);
        m_Input = new Tensor(targetRT, 3);

        preprocessMaterial.mainTexture = targetRT;

        //m_Input = new Tensor(1, inputResolutionY, inputResolutionX, 3);
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

    void Update()
    {
#if (WEBCAM)

        float ratio = (float)m_WebcamTexture.height/(float)m_WebcamTexture.width;

        var targetRT = RenderTexture.GetTemporary(inputResolutionX, inputResolutionY, 0);
        Graphics.Blit(m_WebcamTexture, targetRT, new Vector2(ratio,1f), new Vector2(0f,0f));
        
        var result = new Texture2D (inputResolutionX, inputResolutionY, TextureFormat.RGB24, false);
        result.SetPixels(m_WebcamTexture.GetPixels(0,0,inputResolutionX, inputResolutionY));
        Tensor input = TransformInput(result.GetPixels32(), inputResolutionX, inputResolutionY);
#else
        Tensor input = m_Input;
#endif
        m_Worker.Execute(input);
        Tensor yoloModelOutput = m_Worker.PeekOutput();

        var boxes = new List<BoundingBox>();

        for (int cy = 0; cy < COL_COUNT; cy++)
        {
            for (int cx = 0; cx < ROW_COUNT; cx++)
            {
                for (int box = 0; box < BOXES_PER_CELL; box++)
                {
                    var channel = (box * (CLASS_COUNT + BOX_INFO_FEATURE_COUNT));
                    var bbd = ExtractBoundingBoxDimensions(yoloModelOutput, cx, cy, channel);
                    float confidence = GetConfidence(yoloModelOutput, cx, cy, channel);

                    if (confidence < confidenceThreshold)
                    {
                        continue;
                    }

                    float[] predictedClasses = ExtractClasses(yoloModelOutput, cx, cy, channel);
                    var (topResultIndex, topResultScore) = GetTopResult(predictedClasses);
                    var topScore = topResultScore * confidence;

                    if (topScore < confidenceThreshold)
                    {
                        continue;
                    }

                    var mappedBoundingBox = MapBoundingBoxToCell(cx, cy, box, bbd);
                    boxes.Add(new BoundingBox
                    {
                        Dimensions = new BoundingBoxDimensions
                        {
                            X = (mappedBoundingBox.X - mappedBoundingBox.Width / 2),
                            Y = (mappedBoundingBox.Y - mappedBoundingBox.Height / 2),
                            Width = mappedBoundingBox.Width,
                            Height = mappedBoundingBox.Height,
                        },
                        Confidence = topScore,
                        Label = ((Classes) topResultIndex).ToString()
                    });

                    print(((Classes) topResultIndex).ToString());

                    /*
                    var classes = new float[CLASS_COUNT];
                    for (int c = 0; c < CLASS_COUNT; c++)
                    {
                        // classes[c] = result[offset(channel + 5 + c, cx, cy)];
                        classes[c] = (float)tensor[GetOffset(cx, cy, channel + 5 + c)];
                        // classes[c] = (float)result[tensor.Index(0, cy, cx, channel + 5 + c)];
                    }

                    //softmax this
                    classes = Softmax(classes);
                    // var z_exp = classes.Select(Mathf.Exp);
                    // var sum_z_exp = z_exp.Sum();
                    // classes = z_exp.Select(i => i / sum_z_exp).ToArray();

                    //argmax
                    float bestClassScore = classes.Max();
                    int bestClass = classes.ToList().IndexOf(bestClassScore);

                    float confidenceInClass = bestClassScore * confidence;


                    if(confidenceInClass > confidenceThreshold)
                    {
                        print($"{(Classes)bestClass} : {(confidenceInClass*100).ToString("00")}% ({x};{y};{w};{h})");
                        print(bestClassScore);

                        results.Add(new YoloResult(){
                            x = x,
                            y = y,
                            width = w,
                            height = h,
                            confidence = confidence,
                            classes = classes,
                        });
                    }
                    */
                }
            }

        }


        print(boxes.Count);

        /*
        RenderTexture resultMask = new RenderTexture(inputResolutionX, inputResolutionY, 0);
        resultMask.enableRandomWrite = true;
        resultMask.Create();

        result.ToRenderTexture(resultMask);
        */

        //postprocessMaterial.mainTexture = resultMask;
#if (WEBCAM)
        preprocessMaterial.mainTexture = targetRT;
#endif
        input.Dispose();
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


    private BoundingBoxDimensions ExtractBoundingBoxDimensions(Tensor modelOutput, int x, int y, int channel)
    {
        return new BoundingBoxDimensions
        {
            X = modelOutput[0, x, y, channel],
            Y = modelOutput[0, x, y, channel + 1],
            Width = modelOutput[0, x, y, channel + 2],
            Height = modelOutput[0, x, y, channel + 3]
        };
    }


    private float GetConfidence(Tensor modelOutput, int x, int y, int channel)
    {
        return Sigmoid(modelOutput[0, x, y, channel + 4]);
    }


    private CellDimensions MapBoundingBoxToCell(int x, int y, int box, BoundingBoxDimensions boxDimensions)
    {
        return new CellDimensions
        {
            X = ((float)y + Sigmoid(boxDimensions.X)) * CELL_WIDTH,
            Y = ((float)x + Sigmoid(boxDimensions.Y)) * CELL_HEIGHT,
            Width = (float)Math.Exp(boxDimensions.Width) * CELL_WIDTH * anchors[box * 2],
            Height = (float)Math.Exp(boxDimensions.Height) * CELL_HEIGHT * anchors[box * 2 + 1],
        };
    }


    public float[] ExtractClasses(Tensor modelOutput, int x, int y, int channel)
    {
        float[] predictedClasses = new float[CLASS_COUNT];
        int predictedClassOffset = channel + BOX_INFO_FEATURE_COUNT;

        for (int predictedClass = 0; predictedClass < CLASS_COUNT; predictedClass++)
        {
            predictedClasses[predictedClass] = modelOutput[0, x, y, predictedClass + predictedClassOffset];
        }

        return Softmax(predictedClasses);
    }


    private ValueTuple<int, float> GetTopResult(float[] predictedClasses)
    {
        return predictedClasses
            .Select((predictedClass, index) => (Index: index, Value: predictedClass))
            .OrderByDescending(result => result.Value)
            .First();
    }
    internal struct YoloResult{
        internal float x,y,width,height,confidence;
        internal float[] classes;
    }

    public class DimensionsBase
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Height { get; set; }
        public float Width { get; set; }
    }


    public class BoundingBoxDimensions : DimensionsBase { }

    class CellDimensions : DimensionsBase { }


    public class BoundingBox
    {
        public BoundingBoxDimensions Dimensions { get; set; }

        public string Label { get; set; }

        public float Confidence { get; set; }

        public Rect Rect
        {
            get { return new Rect(Dimensions.X, Dimensions.Y, Dimensions.Width, Dimensions.Height); }
        }

        public override string ToString()
        {
            return $"{Label}:{Confidence}, {Dimensions.X}:{Dimensions.Y} - {Dimensions.Width}:{Dimensions.Height}";
        }
    }

    public const int ROW_COUNT = 13;
    public const int COL_COUNT = 13;
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
