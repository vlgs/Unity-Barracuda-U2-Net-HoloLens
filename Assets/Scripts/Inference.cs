using System;
using System.Runtime.InteropServices;
using UnityEngine;

public class Inference : MonoBehaviour
{
    [SerializeField] private NeuralModel m_model;
    [SerializeField] private VideoSource m_videoSource;
    
    void Start()
    {
        Application.targetFrameRate = 60;

        m_videoSource.Init();
        m_model.Init();
    }

    void Update()
    {
        var frame = m_videoSource.GrabFrame();
        m_model.Infer(frame);

        //Show reesult ??
    }

    void OnDestroy()
    {
        m_model.Dispose();
        m_videoSource.Dispose();
    }

}
