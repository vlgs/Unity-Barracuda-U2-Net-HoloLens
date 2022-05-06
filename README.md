# Unity Barracuda Machine Learning U2Net Neural Network on HoloLens 2  
## Description  
This is a test project for implementing U-2-Net segmentation network with Unity Barracuda, on HoloLens 2.  

## Instructions  
### Running the sample
1. Clone repo  
2. Switch target to UWP  
3. Open SampleScene 
4. Hit play in the editor or build

### Changing source  
This project support both VideoCapture from Unity or a mere image.  
  
To use an image as un input:  
1. Remove the first line from Inference.cs "#define WEBCAM"  
2. Select a Texture on the Inference component on "Main Camera" in SampleScene  
