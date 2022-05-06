//
// Copyright (c) 2017 Vulcan, Inc. All rights reserved.  
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.
//

#if UNITY_WSA && ENABLE_WINMD_SUPPORT
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;

using Windows.Graphics.Imaging;

using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Media.Effects;
using Windows.Perception.Spatial;

namespace HoloLensCameraStream
{
    /// <summary>
    /// Called when a VideoCapture resource has been created.
    /// If the instance failed to be created, the instance returned will be null.
    /// </summary>
    /// <param name="captureObject">The VideoCapture instance.</param>
    public delegate void OnVideoCaptureResourceCreatedCallback(VideoCapture captureObject);

    /// <summary>
    /// Called when the web camera begins streaming video.
    /// </summary>
    /// <param name="result">Indicates whether or not video recording started successfully.</param>
    public delegate void OnVideoModeStartedCallback(VideoCaptureResult result);

    /// <summary>
    /// This is called every time there is a new frame sample available.
    /// See VideoCapture.FrameSampleAcquired and the VideoCaptureSample class for more information.
    /// </summary>
    /// <param name="videoCaptureSample">The recently captured frame sample.
    /// It contains methods for accessing the bitmap, as well as supporting information
    /// such as transform and projection matrices.</param>
    public delegate void FrameSampleAcquiredCallback(VideoCaptureSample videoCaptureSample);

    /// <summary>
    /// Called when video mode has been stopped.
    /// </summary>
    /// <param name="result">Indicates whether or not video mode was successfully deactivated.</param>
    public delegate void OnVideoModeStoppedCallback(VideoCaptureResult result);

    /// <summary>
    /// Streams video from the camera and makes the buffer available for reading.
    /// </summary>
    public sealed class VideoCapture
    {
        /// <summary>
        /// Note: This function is not yet implemented. Help us out on GitHub!
        /// There is an instance method on VideoCapture called GetSupportedResolutions().
        /// Please use that until we can get this method working.
        /// </summary>
        public static IEnumerable<Resolution> SupportedResolutions
        {
            get
            {
                throw new NotImplementedException("Please use the instance method VideoCapture.GetSupportedResolutions() for now.");
            }
        }

        /// <summary>
        /// Returns the supported frame rates at which a video can be recorded given a resolution.
        /// Use VideoCapture.SupportedResolutions to get the supported web camera recording resolutions.
        /// </summary>
        /// <param name="resolution">A recording resolution.</param>
        /// <returns>The frame rates at which the video can be recorded.</returns>
        public static IEnumerable<float> SupportedFrameRatesForResolution(Resolution resolution)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// This is called every time there is a new frame sample available.
        /// You must properly initialize the VideoCapture object, including calling StartVideoModeAsync()
        /// before this event will begin firing.
        /// 
        /// You should not subscribe to FrameSampleAcquired if you do not need access to most
        /// of the video frame samples for your application (for instance, if you are doing image detection once per second),
        /// because there is significant memory management overhead to processing every frame.
        /// Instead, you can call RequestNextFrameSample() which will respond with the next available sample only.
        /// 
        /// See the VideoFrameSample class for more information about dealing with the memory
        /// complications of the BitmapBuffer.
        /// </summary>
        public event FrameSampleAcquiredCallback FrameSampleAcquired;

        /// <summary>
        /// Indicates whether or not the VideoCapture instance is currently streaming video.
        /// This becomes true when the OnVideoModeStartedCallback is called, and ends 
        /// when the OnVideoModeStoppedCallback is called.
        /// 
        /// "VideoMode", as I have interpreted means that the frame reader begins delivering
        /// the bitmap buffer, making it available to be consumed.
        /// </summary>
        public bool IsStreaming
        {
            get
            {
                return _frameReader != null;
            }
        }

        internal SpatialCoordinateSystem worldOrigin { get; private set; }
        public IntPtr WorldOriginPtr
        {
            set
            {
                worldOrigin = (SpatialCoordinateSystem)Marshal.GetObjectForIUnknown(value);
            }
        }

        static readonly MediaStreamType STREAM_TYPE = MediaStreamType.VideoPreview;
        static readonly Guid ROTATION_KEY = new Guid("C380465D-2271-428C-9B83-ECEA3B4A85C1");

        MediaFrameSourceGroup _frameSourceGroup;
        MediaFrameSourceInfo _frameSourceInfo;
        DeviceInformation _deviceInfo;
        MediaCapture _mediaCapture;
        MediaFrameReader _frameReader;

        VideoCapture(MediaFrameSourceGroup frameSourceGroup, MediaFrameSourceInfo frameSourceInfo, DeviceInformation deviceInfo)
        {
            _frameSourceGroup = frameSourceGroup;
            _frameSourceInfo = frameSourceInfo;
            _deviceInfo = deviceInfo;
        }

        /// <summary>
        /// Asynchronously creates an instance of a VideoCapture object that can be used to stream video frames from the camera to memory.
        /// If the instance failed to be created, the instance returned will be null. Also, holograms will not appear in the video.
        /// </summary>
        /// <param name="onCreatedCallback">This callback will be invoked when the VideoCapture instance is created and ready to be used.</param>
        public static async void CreateAync(OnVideoCaptureResourceCreatedCallback onCreatedCallback)
        {
            var allFrameSourceGroups = await MediaFrameSourceGroup.FindAllAsync();                                              //Returns IReadOnlyList<MediaFrameSourceGroup>
            var candidateFrameSourceGroups = allFrameSourceGroups.Where(group => group.SourceInfos.Any(IsColorVideo));   //Returns IEnumerable<MediaFrameSourceGroup>
            var selectedFrameSourceGroup = candidateFrameSourceGroups.FirstOrDefault();                                         //Returns a single MediaFrameSourceGroup

            if (selectedFrameSourceGroup == null)
            {
                onCreatedCallback?.Invoke(null);
                return;
            }

            var selectedFrameSourceInfo = selectedFrameSourceGroup.SourceInfos.FirstOrDefault(); //Returns a MediaFrameSourceInfo

            if (selectedFrameSourceInfo == null)
            {
                onCreatedCallback?.Invoke(null);
                return;
            }

            var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);   //Returns DeviceCollection
            var deviceInformation = devices.FirstOrDefault();                               //Returns a single DeviceInformation

            if (deviceInformation == null)
            {
                onCreatedCallback(null);
                return;
            }

            var videoCapture = new VideoCapture(selectedFrameSourceGroup, selectedFrameSourceInfo, deviceInformation);
            await videoCapture.CreateMediaCaptureAsync();
            onCreatedCallback?.Invoke(videoCapture);
        }

        public IEnumerable<Resolution> GetSupportedResolutions()
        {
            List<Resolution> resolutions = new List<Resolution>();

            var allPropertySets = _mediaCapture.VideoDeviceController.GetAvailableMediaStreamProperties(STREAM_TYPE).Select(x => x as VideoEncodingProperties); //Returns IEnumerable<VideoEncodingProperties>
            foreach (var propertySet in allPropertySets)
            {
                resolutions.Add(new Resolution((int)propertySet.Width, (int)propertySet.Height));
            }

            return resolutions.AsReadOnly();
        }

        public IEnumerable<float> GetSupportedFrameRatesForResolution(Resolution resolution)
        {
            //Get all property sets that match the supported resolution
            var allPropertySets = _mediaCapture.VideoDeviceController.GetAvailableMediaStreamProperties(STREAM_TYPE).Select((x) => x as VideoEncodingProperties)
                .Where((x) =>
                {
                    return x != null &&
                    x.Width == (uint)resolution.width &&
                    x.Height == (uint)resolution.height;
                }); //Returns IEnumerable<VideoEncodingProperties>

            //Get all resolutions without duplicates.
            var frameRatesDict = new Dictionary<float, bool>();
            foreach (var propertySet in allPropertySets)
            {
                if (propertySet.FrameRate.Denominator != 0)
                {
                    float frameRate = (float)propertySet.FrameRate.Numerator / (float)propertySet.FrameRate.Denominator;
                    frameRatesDict.Add(frameRate, true);
                }
            }

            //Format resolutions as a list.
            var frameRates = new List<float>();
            foreach (KeyValuePair<float, bool> kvp in frameRatesDict)
            {
                frameRates.Add(kvp.Key);
            }

            return frameRates.AsReadOnly();
        }

        /// <summary>
        /// Asynchronously starts video mode.
        /// 
        /// Activates the web camera with the various settings specified in CameraParameters.
        /// Only one VideoCapture instance can start the video mode at any given time.
        /// After starting the video mode, you listen for new video frame samples via the VideoCapture.FrameSampleAcquired event, 
        /// or by calling VideoCapture.RequestNextFrameSample() when will return the next available sample.
        /// While in video mode, more power will be consumed so make sure that you call VideoCapture.StopVideoModeAsync qhen you can afford the start/stop video mode overhead.
        /// </summary>
        /// <param name="setupParams">Parameters that change how video mode is used.</param>
        /// <param name="onVideoModeStartedCallback">This callback will be invoked once video mode has been activated.</param>
        public async void StartVideoModeAsync(CameraParameters setupParams, OnVideoModeStartedCallback onVideoModeStartedCallback)
        {
            var mediaFrameSource = _mediaCapture.FrameSources[_frameSourceInfo.Id]; //Returns a MediaFrameSource

            if (mediaFrameSource == null)
            {
                onVideoModeStartedCallback?.Invoke(new VideoCaptureResult(1, ResultType.UnknownError, false));
                return;
            }

            var pixelFormat = ConvertCapturePixelFormatToMediaEncodingSubtype(setupParams.pixelFormat);
            _frameReader = await _mediaCapture.CreateFrameReaderAsync(mediaFrameSource, pixelFormat);
            _frameReader.FrameArrived += HandleFrameArrived;
            await _frameReader.StartAsync();
            VideoEncodingProperties properties = GetVideoEncodingPropertiesForCameraParams(setupParams);

            // Historical context: https://github.com/VulcanTechnologies/HoloLensCameraStream/issues/6
            if (setupParams.rotateImage180Degrees)
            {
                properties.Properties.Add(ROTATION_KEY, 180);
            }

            //	gr: taken from here https://forums.hololens.com/discussion/2009/mixedrealitycapture
            IVideoEffectDefinition ved = new VideoMRCSettings(setupParams.enableHolograms, setupParams.enableVideoStabilization, setupParams.videoStabilizationBufferSize, setupParams.hologramOpacity);
            await _mediaCapture.AddVideoEffectAsync(ved, MediaStreamType.VideoPreview);

            await _mediaCapture.VideoDeviceController.SetMediaStreamPropertiesAsync(STREAM_TYPE, properties);

            onVideoModeStartedCallback?.Invoke(new VideoCaptureResult(0, ResultType.Success, true));
        }

        /// <summary>
        /// Returns a new VideoFrameSample as soon as the next one is available.
        /// This method is preferable to listening to the FrameSampleAcquired event
        /// in circumstances where most or all frames are not needed. For instance, if
        /// you were planning on sending frames to a remote image recognition service twice per second,
        /// you may consider using this method rather than ignoring most of the event dispatches from FrameSampleAcquired.
        /// This will avoid the overhead of acquiring and disposing of unused frames.
        /// 
        /// If, for whatever reason, a frame reference cannot be obtained, it is possible that the callback will return a null sample.
        /// </summary>
        /// <param name="onFrameSampleAcquired"></param>
        public void RequestNextFrameSample(FrameSampleAcquiredCallback onFrameSampleAcquired)
        {
            if (onFrameSampleAcquired == null)
            {
                throw new ArgumentNullException("onFrameSampleAcquired");
            }

            if (IsStreaming == false)
            {
                throw new Exception("You cannot request a frame sample until the video mode is started.");
            }

            TypedEventHandler<MediaFrameReader, MediaFrameArrivedEventArgs> handler = null;
            handler = (MediaFrameReader sender, MediaFrameArrivedEventArgs args) =>
            {
                using (var frameReference = _frameReader.TryAcquireLatestFrame()) //frame: MediaFrameReference
                {
                    if (frameReference != null)
                    {
                        onFrameSampleAcquired.Invoke(new VideoCaptureSample(frameReference, worldOrigin));
                    }
                    else
                    {
                        onFrameSampleAcquired.Invoke(null);
                    }
                }
                _frameReader.FrameArrived -= handler;
            };
            _frameReader.FrameArrived += handler;
        }

        /// <summary>
        /// Asynchronously stops video mode.
        /// </summary>
        /// <param name="onVideoModeStoppedCallback">This callback will be invoked once video mode has been deactivated.</param>
        public async void StopVideoModeAsync(OnVideoModeStoppedCallback onVideoModeStoppedCallback)
        {
            if (IsStreaming == false)
            {
                onVideoModeStoppedCallback?.Invoke(new VideoCaptureResult(1, ResultType.InappropriateState, false));
                return;
            }

            _frameReader.FrameArrived -= HandleFrameArrived;
            await _frameReader.StopAsync();
            _frameReader.Dispose();
            _frameReader = null;

            onVideoModeStoppedCallback?.Invoke(new VideoCaptureResult(0, ResultType.Success, true));
        }

        /// <summary>
        /// Dispose must be called to shutdown the PhotoCapture instance.
        /// 
        /// If your VideoCapture instance successfully called VideoCapture.StartVideoModeAsync,
        /// you must make sure that you call VideoCapture.StopVideoModeAsync before disposing your VideoCapture instance.
        /// </summary>
        public void Dispose()
        {
            if (IsStreaming)
            {
                throw new Exception("Please make sure StopVideoModeAsync() is called before displosing the VideoCapture object.");
            }

            _mediaCapture?.Dispose();
        }

        async Task CreateMediaCaptureAsync()
        {
            if (_mediaCapture != null)
            {
                throw new Exception("The MediaCapture object has already been created.");
            }

            _mediaCapture = new MediaCapture();
            await _mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings()
            {
                VideoDeviceId = _deviceInfo.Id,
                SourceGroup = _frameSourceGroup,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu, //TODO: Should this be the other option, Auto? GPU is not an option.
                StreamingCaptureMode = StreamingCaptureMode.Video
            });
            _mediaCapture.VideoDeviceController.Focus.TrySetAuto(true);
        }

        void HandleFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            if (FrameSampleAcquired == null)
            {
                return;
            }

            using (var frameReference = _frameReader.TryAcquireLatestFrame()) //frameReference is a MediaFrameReference
            {
                if (frameReference != null)
                {
                    var sample = new VideoCaptureSample(frameReference, worldOrigin);
                    FrameSampleAcquired?.Invoke(sample);
                }
            }
        }

        VideoEncodingProperties GetVideoEncodingPropertiesForCameraParams(CameraParameters cameraParams)
        {
            var allPropertySets = _mediaCapture.VideoDeviceController.GetAvailableMediaStreamProperties(STREAM_TYPE).Select((x) => x as VideoEncodingProperties)
                .Where((x) =>
                {
                    if (x == null) return false;
                    if (x.FrameRate.Denominator == 0) return false;

                    double calculatedFrameRate = (double)x.FrameRate.Numerator / (double)x.FrameRate.Denominator;

                    return
                    x.Width == (uint)cameraParams.cameraResolutionWidth &&
                    x.Height == (uint)cameraParams.cameraResolutionHeight &&
                    (int)Math.Round(calculatedFrameRate) == cameraParams.frameRate;
                }); //Returns IEnumerable<VideoEncodingProperties>

            if (allPropertySets.Count() == 0)
            {
                throw new Exception("Could not find an encoding property set that matches the given camera parameters.");
            }

            var chosenPropertySet = allPropertySets.FirstOrDefault();
            return chosenPropertySet;
        }

        static bool IsColorVideo(MediaFrameSourceInfo sourceInfo)
        {
            //TODO: Determine whether 'VideoPreview' or 'VideoRecord' is the appropriate type. What's the difference?
            return (sourceInfo.MediaStreamType == STREAM_TYPE &&
                sourceInfo.SourceKind == MediaFrameSourceKind.Color);
        }

        static string ConvertCapturePixelFormatToMediaEncodingSubtype(CapturePixelFormat format)
        {
            switch (format)
            {
                case CapturePixelFormat.BGRA32:
                    return MediaEncodingSubtypes.Bgra8;
                case CapturePixelFormat.NV12:
                    return MediaEncodingSubtypes.Nv12;
                case CapturePixelFormat.JPEG:
                    return MediaEncodingSubtypes.Jpeg;
                case CapturePixelFormat.PNG:
                    return MediaEncodingSubtypes.Png;
                default:
                    return MediaEncodingSubtypes.Bgra8;
            }
        }
    }


    //	from https://forums.hololens.com/discussion/2009/mixedrealitycapture
    public class VideoMRCSettings : IVideoEffectDefinition
    {
        public string ActivatableClassId
        {
            get
            {
                return "Windows.Media.MixedRealityCapture.MixedRealityCaptureVideoEffect";
            }
        }

        public IPropertySet Properties
        {
            get; private set;
        }

        public VideoMRCSettings(bool HologramCompositionEnabled, bool VideoStabilizationEnabled, int VideoStabilizationBufferLength, float GlobalOpacityCoefficient)
        {
            Properties = (IPropertySet)new PropertySet();
            Properties.Add("HologramCompositionEnabled", HologramCompositionEnabled);
            Properties.Add("VideoStabilizationEnabled", VideoStabilizationEnabled);
            Properties.Add("VideoStabilizationBufferLength", VideoStabilizationBufferLength);
            Properties.Add("GlobalOpacityCoefficient", GlobalOpacityCoefficient);
        }
    }

    /// <summary>
    /// Represents the reason why the callback fired.
    /// </summary>
    public enum ResultType
    {
        /// <summary>
        /// Everything went okay, continue down the happy path.
        /// </summary>
        Success,
        /// <summary>
        /// A function was called when the VideoCapture object when in the wrong state.
        /// For instance, alling StopVideoModeAsync() when video mode is already stopped
        /// will result in an early calling of the callback as the video mode does not need
        /// time to be stopped.
        /// </summary>
        InappropriateState,

        /// <summary>
        /// Something went wrong when performing the async operation.
        /// VideoCapture should not be considered a stable, usable object.
        /// </summary>
        UnknownError
    }

    public struct VideoCaptureResult
    {
        /// <summary>
        /// Not really used. Set to 1 when success=false, and is 0 when success=true
        /// </summary>
        public readonly long hResult;

        /// <summary>
        /// Represents the reason why the callback fired.
        /// </summary>
        public readonly ResultType resultType;

        /// <summary>
        /// A simple answer of whether or not everything worked out with the async process.
        /// </summary>
        public readonly bool success;

        internal VideoCaptureResult(long hResult, ResultType resultType, bool success)
        {
            this.hResult = hResult;
            this.resultType = resultType;
            this.success = success;
        }
    }
    public class VideoCaptureSample
    {
        /// <summary>
        /// The guid for getting the view transform from the frame sample.
        /// See https://developer.microsoft.com/en-us/windows/mixed-reality/locatable_camera#locating_the_device_camera_in_the_world
        /// </summary>
        static Guid viewTransformGuid = new Guid("4E251FA4-830F-4770-859A-4B8D99AA809B");

        /// <summary>
        /// The guid for getting the projection transform from the frame sample.
        /// See https://developer.microsoft.com/en-us/windows/mixed-reality/locatable_camera#locating_the_device_camera_in_the_world
        /// </summary>
        static Guid projectionTransformGuid = new Guid("47F9FCB5-2A02-4F26-A477-792FDF95886A");

        /// <summary>
        /// The guid for getting the camera coordinate system for the frame sample.
        /// See https://developer.microsoft.com/en-us/windows/mixed-reality/locatable_camera#locating_the_device_camera_in_the_world
        /// </summary>
        static Guid cameraCoordinateSystemGuid = new Guid("9D13C82F-2199-4E67-91CD-D1A4181F2534");

        /// <summary>
        /// How many bytes are in the frame.
        /// There are four bytes per pixel, times the width and height of the bitmap.
        /// </summary>
        public int dataLength
        {
            get
            {
                return 4 * bitmap.PixelHeight * bitmap.PixelWidth;
            }
        }

        /// <summary>
        /// Note: This method has not been written. Help us out on GitHub!
        /// Will be true if the HoloLens knows where it is and is tracking.
        /// Indicates that obtaining the matrices will be successful.
        /// </summary>
        public bool hasLocationData
        {
            get
            {
                //TODO: Return if location data exists.
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// The format of the frames that the bitmap stream is sending.
        /// </summary>
        public CapturePixelFormat pixelFormat { get; private set; }

        //Internal members

        internal SpatialCoordinateSystem worldOrigin { get; private set; }

        internal SoftwareBitmap bitmap { get; private set; }

        internal bool isBitmapCopied { get; private set; }

        //Private members

        MediaFrameReference frameReference;

        internal VideoCaptureSample(MediaFrameReference frameReference, SpatialCoordinateSystem worldOrigin)
        {
            if (frameReference == null)
            {
                throw new ArgumentNullException("frameReference.");
            }

            this.frameReference = frameReference;
            this.worldOrigin = worldOrigin;

            bitmap = frameReference.VideoMediaFrame.SoftwareBitmap;
        }

        /// <summary>
        /// If you need safe, long term control over the image bytes in this frame, they will need to be
        /// copied. You need to supply a byte[] to copy them into. It is best to pre-allocate and reuse
        /// this byte array to minimize unecessarily high memory ceiling or unnecessary garbage collections.
        /// </summary>
        /// <param name="byteBuffer">A byte array with a length the size of VideoCaptureSample.dataLength</param>
        public void CopyRawImageDataIntoBuffer(byte[] byteBuffer)
        {
            //Here is a potential way to get direct access to the buffer:
            //http://stackoverflow.com/questions/25481840/how-to-change-mediacapture-to-byte

            if (byteBuffer == null)
            {
                throw new ArgumentNullException("byteBuffer");
            }

            if (byteBuffer.Length < dataLength)
            {
                throw new IndexOutOfRangeException("Your byteBuffer is not big enough." +
                    " Please use the VideoCaptureSample.dataLength property to allocate a large enough array.");
            }

            bitmap.CopyToBuffer(byteBuffer.AsBuffer());
            isBitmapCopied = true;
        }


        public void CopyRawImageDataIntoBuffer(List<byte> byteBuffer)
        {
            throw new NotSupportedException("This method is not yet supported with a List<byte>. Please provide a byte[] instead.");
        }

        /// <summary>
        /// This returns the transform matrix at the time the photo was captured, if location data if available.
        /// If it's not, that is probably an indication that the HoloLens is not tracking and its location is not known.
        /// It could also mean the VideoCapture stream is not running.
        /// If location data is unavailable then the camera to world matrix will be set to the identity matrix.
        /// </summary>
        /// <param name="matrix">The transform matrix used to convert between coordinate spaces.
        /// The matrix will have to be converted to a Unity matrix before it can be used by methods in the UnityEngine namespace.
        /// See https://forum.unity3d.com/threads/locatable-camera-in-unity.398803/ for details.</param>
        public bool TryGetCameraToWorldMatrix(out float[] outMatrix)
        {
            if (frameReference.Properties.ContainsKey(viewTransformGuid) == false)
            {
                outMatrix = GetIdentityMatrixFloatArray();
                return false;
            }

            if (worldOrigin == null)
            {
                outMatrix = GetIdentityMatrixFloatArray();
                return false;
            }

            Matrix4x4 cameraViewTransform = ConvertByteArrayToMatrix4x4(frameReference.Properties[viewTransformGuid] as byte[]);
            if (cameraViewTransform == null)
            {
                outMatrix = GetIdentityMatrixFloatArray();
                return false;
            }

            SpatialCoordinateSystem cameraCoordinateSystem = frameReference.Properties[cameraCoordinateSystemGuid] as SpatialCoordinateSystem;
            if (cameraCoordinateSystem == null)
            {
                outMatrix = GetIdentityMatrixFloatArray();
                return false;
            }

            Matrix4x4? cameraCoordsToUnityCoordsMatrix = cameraCoordinateSystem.TryGetTransformTo(worldOrigin);
            if (cameraCoordsToUnityCoordsMatrix == null)
            {
                outMatrix = GetIdentityMatrixFloatArray();
                return false;
            }

            // Transpose the matrices to obtain a proper transform matrix
            cameraViewTransform = Matrix4x4.Transpose(cameraViewTransform);
            Matrix4x4 cameraCoordsToUnityCoords = Matrix4x4.Transpose(cameraCoordsToUnityCoordsMatrix.Value);

            Matrix4x4 viewToWorldInCameraCoordsMatrix;
            Matrix4x4.Invert(cameraViewTransform, out viewToWorldInCameraCoordsMatrix);
            Matrix4x4 viewToWorldInUnityCoordsMatrix = Matrix4x4.Multiply(cameraCoordsToUnityCoords, viewToWorldInCameraCoordsMatrix);

            // Change from right handed coordinate system to left handed UnityEngine
            viewToWorldInUnityCoordsMatrix.M31 *= -1f;
            viewToWorldInUnityCoordsMatrix.M32 *= -1f;
            viewToWorldInUnityCoordsMatrix.M33 *= -1f;
            viewToWorldInUnityCoordsMatrix.M34 *= -1f;

            outMatrix = ConvertMatrixToFloatArray(viewToWorldInUnityCoordsMatrix);

            return true;
        }

        /// <summary>
        /// This returns the projection matrix at the time the photo was captured, if location data if available.
        /// If it's not, that is probably an indication that the HoloLens is not tracking and its location is not known.
        /// It could also mean the VideoCapture stream is not running.
        /// If location data is unavailable then the projecgtion matrix will be set to the identity matrix.
        /// </summary>
        /// <param name="matrix">The projection matrix used to match the true camera projection.
        /// The matrix will have to be converted to a Unity matrix before it can be used by methods in the UnityEngine namespace.
        /// See https://forum.unity3d.com/threads/locatable-camera-in-unity.398803/ for details.</param>
        public bool TryGetProjectionMatrix(out float[] outMatrix)
        {
            if (frameReference.Properties.ContainsKey(projectionTransformGuid) == false)
            {
                outMatrix = GetIdentityMatrixFloatArray();
                return false;
            }

            Matrix4x4 projectionMatrix = ConvertByteArrayToMatrix4x4(frameReference.Properties[projectionTransformGuid] as byte[]);

            // Transpose matrix to match expected Unity format
            projectionMatrix = Matrix4x4.Transpose(projectionMatrix);
            outMatrix = ConvertMatrixToFloatArray(projectionMatrix);
            return true;
        }

        /// <summary>
        /// Note: This method hasn't been written yet. Help us out on GitHub!
        /// </summary>
        /// <param name="targetTexture"></param>
        public void UploadImageDataToTexture(object targetTexture)
        {
            //TODO: Figure out how to use a Texture2D in a plugin.
            throw new NotSupportedException("I'm not sure how to use a Texture2D within this plugin.");
        }

        /// <summary>
        /// When done with the VideoCapture class, you will need to dispose it to release unmanaged memory.
        /// </summary>
        public void Dispose()
        {
            bitmap.Dispose();
            frameReference.Dispose();
        }

        private float[] ConvertMatrixToFloatArray(Matrix4x4 matrix)
        {
            return new float[16] {
                matrix.M11, matrix.M12, matrix.M13, matrix.M14,
                matrix.M21, matrix.M22, matrix.M23, matrix.M24,
                matrix.M31, matrix.M32, matrix.M33, matrix.M34,
                matrix.M41, matrix.M42, matrix.M43, matrix.M44 };
        }

        private Matrix4x4 ConvertByteArrayToMatrix4x4(byte[] matrixAsBytes)
        {
            if (matrixAsBytes == null)
            {
                throw new ArgumentNullException("matrixAsBytes");
            }

            if (matrixAsBytes.Length != 64)
            {
                throw new Exception("Cannot convert byte[] to Matrix4x4. Size of array should be 64, but it is " + matrixAsBytes.Length);
            }

            var m = matrixAsBytes;
            return new Matrix4x4(
                BitConverter.ToSingle(m, 0),
                BitConverter.ToSingle(m, 4),
                BitConverter.ToSingle(m, 8),
                BitConverter.ToSingle(m, 12),
                BitConverter.ToSingle(m, 16),
                BitConverter.ToSingle(m, 20),
                BitConverter.ToSingle(m, 24),
                BitConverter.ToSingle(m, 28),
                BitConverter.ToSingle(m, 32),
                BitConverter.ToSingle(m, 36),
                BitConverter.ToSingle(m, 40),
                BitConverter.ToSingle(m, 44),
                BitConverter.ToSingle(m, 48),
                BitConverter.ToSingle(m, 52),
                BitConverter.ToSingle(m, 56),
                BitConverter.ToSingle(m, 60));
        }

        static CapturePixelFormat ConvertBitmapPixelFormatToCapturePixelFormat(BitmapPixelFormat format)
        {
            switch (format)
            {
                case BitmapPixelFormat.Bgra8:
                    return CapturePixelFormat.BGRA32;
                case BitmapPixelFormat.Nv12:
                    return CapturePixelFormat.NV12;
                default:
                    return CapturePixelFormat.Unknown;
            }
        }

        static byte[] GetIdentityMatrixByteArray()
        {
            return new byte[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
        }

        static float[] GetIdentityMatrixFloatArray()
        {
            return new float[] { 1f, 0, 0, 0, 0, 1f, 0, 0, 0, 0, 1f, 0, 0, 0, 0, 1f };
        }
    }

    /// <summary>
    /// The pixel format used to capture and record your image data.
    /// </summary>
    public enum CapturePixelFormat
    {
        BGRA32,
        NV12,
        JPEG,
        PNG,
        Unknown
    }

    public struct Resolution
    {
        public readonly int width;
        public readonly int height;

        public Resolution(int width, int height)
        {
            this.width = width;
            this.height = height;
        }
    }

    public struct CameraParameters
    {
        public CapturePixelFormat pixelFormat;

        public int cameraResolutionHeight;

        public int cameraResolutionWidth;

        public int frameRate;

        public bool rotateImage180Degrees;

        public float hologramOpacity;
        public bool enableHolograms
        {
            get { throw new NotImplementedException(); }
            set { throw new NotImplementedException(); }
        }

        public int videoStabilizationBufferSize;
        public bool enableVideoStabilization
        {
            get { throw new NotImplementedException(); }
            set { throw new NotImplementedException(); }
        }

        public CameraParameters(
            CapturePixelFormat pixelFormat = CapturePixelFormat.BGRA32,
            int cameraResolutionHeight = 720,
            int cameraResolutionWidth = 1280,
            int frameRate = 30,
            bool rotateImage180Degrees = true)
        { throw new NotImplementedException(); }
    }

}
#endif