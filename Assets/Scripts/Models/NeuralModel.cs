using System;
using Unity.Barracuda;
using UnityEngine;


public abstract class NeuralModel : ScriptableObject, IDisposable
{
    [SerializeField] protected NNModel m_inputModel;
    [SerializeField] protected int m_inputResolutionHeight = 32;
    [SerializeField] protected int m_inputResolutionWidth = 32;
    [SerializeField] protected string m_outputLayerName = "016_convolutional";
    [Range(0, 1), SerializeField] protected float m_confidenceThreshold = 0.3f;

    [Header("pre/post materials"),
     SerializeField] protected Material m_preprocessMaterial;
    [SerializeField] protected Material m_postprocessMaterial;

    protected Model m_runtimeModel;
    protected IWorker m_worker;

    internal abstract void Init();
    internal abstract void Infer(Texture2D input);
    public abstract void Dispose();
}
