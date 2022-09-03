using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class U2NetModel : NeuralModel
{
    internal override void Init()
    {
        throw new System.NotImplementedException();
    }

    internal override void Infer(Texture2D input)
    {
        throw new System.NotImplementedException();

        /*
        RenderTexture resultMask = new RenderTexture(inputResolutionX, inputResolutionY, 0);
        resultMask.enableRandomWrite = true;
        resultMask.Create();

        result.ToRenderTexture(resultMask);
        */

        //postprocessMaterial.mainTexture = resultMask;
    }

    public override void Dispose()
    {
        throw new System.NotImplementedException();
    }

}
