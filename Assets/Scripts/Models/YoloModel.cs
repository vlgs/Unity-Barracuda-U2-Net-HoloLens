using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Barracuda;
using UnityEngine;

[CreateAssetMenu(fileName = "YoloModel", menuName = "AI/YoloModel", order = 1)]
public class YoloModel : NeuralModel
{
    internal override void Init()
    {
        m_runtimeModel = ModelLoader.Load(m_inputModel, false);
        m_worker = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, m_runtimeModel, false);
    }

    internal override void Infer(Texture2D input)
    {
        //could have been, in a coroutine
        //yield return StartCoroutine(worker.StartManualSchedule(inputs));

        //if neeeed, crop image
        if(input.width != m_inputResolutionWidth || input.height != m_inputResolutionHeight)
            input = input.Crop(m_inputResolutionWidth, m_inputResolutionHeight);

        m_worker.Execute(TransformInput(input.GetPixels32(), m_inputResolutionWidth, m_inputResolutionHeight));
        m_worker.WaitForCompletion();

        Tensor tensor = m_worker.PeekOutput(m_outputLayerName);

        List<YoloResult> results = new List<YoloResult>();

        for (int cx = 0; cx < ROW_COUNT; cx++)
        {
            for (int cy = 0; cy < COL_COUNT; cy++)
            {
                for (int b = 0; b < BOXES_PER_CELL; b++)
                {
                    var channel = (b * (CLASS_COUNT + BOX_INFO_FEATURE_COUNT));

                    float tx = tensor[0, cx, cy, channel];
                    float ty = tensor[0, cx, cy, channel + 1];
                    float tw = tensor[0, cx, cy, channel + 2];
                    float th = tensor[0, cx, cy, channel + 3];
                    float tc = tensor[0, cx, cy, channel + 4];

                    float x = ((float)cy + Sigmoid(tx)) * CELL_WIDTH;
                    float y = ((float)cx + Sigmoid(ty)) * CELL_HEIGHT;

                    float w = Mathf.Exp(tw) * anchors[2 * b] * CELL_WIDTH;
                    float h = Mathf.Exp(th) * anchors[2 * b + 1] * CELL_HEIGHT;

                    float confidence = Sigmoid(tc);

                    if (confidence < m_confidenceThreshold)
                        continue;

                    var classes = new float[CLASS_COUNT];
                    var classOffset = channel + BOX_INFO_FEATURE_COUNT;

                    for (int c = 0; c < CLASS_COUNT; c++)
                    {
                        classes[c] = tensor[0, cx, cy, classOffset + c];
                    }

                    //softmax this
                    classes = Softmax(classes);

                    //argmax
                    float bestClassScore = classes.Max();
                    int bestClass = classes.ToList().IndexOf(bestClassScore);

                    float confidenceInClass = bestClassScore * confidence;

                    if (confidenceInClass > m_confidenceThreshold)
                    {
                        Debug.Log($"{(Classes)bestClass} : {(bestClassScore * 100).ToString("00")}% ({x};{y};{w};{h})");

                        results.Add(new YoloResult()
                        {
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

        Debug.Log($"#### {results.Count}");

        //input.Dispose();
    }


    private const int IMAGE_MEAN = 0;
    private const float IMAGE_STD = 1;

    //could be done in gpu https://stackoverflow.com/questions/50261424/converting-a-texture-to-a-1d-array-of-float-values-using-a-compute-shader
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

    internal struct YoloResult
    {
        internal float x, y, width, height, confidence;
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
    private float[] anchors = new float[] { 1.08f, 1.19f, 3.42f, 4.41f, 6.63f, 11.38f, 9.42f, 5.11f, 16.62f, 10.52f };

    enum Classes
    {
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


    public override void Dispose()
    {
        m_worker.Dispose();
    }
}
