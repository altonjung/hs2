using Studio;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using ToolBox;
using ToolBox.Extensions;
using UILib;
using UnityEngine;
using UnityEngine.SceneManagement;

#if IPA
using Harmony;
using IllusionPlugin;
#elif BEPINEX
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
#endif
#if AISHOUJO || HONEYSELECT2
using CharaUtils;
using ExtensibleSaveFormat;
using AIChara;
using System.Security.Cryptography;
using KKAPI.Studio;
using IllusionUtility.GetUtility;
#endif

#if AISHOUJO || HONEYSELECT2
using AIChara;
#endif

namespace ChaCardSupport
{
#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if KOIKATSU || SUNSHINE
    [BepInProcess("CharaStudio")]
#elif AISHOUJO || HONEYSELECT2
    [BepInProcess("HoneySelect2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class ChaCardSupport : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "ChaCardSupport";
        public const string Version = "0.9.0.0";
        public const string GUID = "com.alton.illusionplugins.chacardsupport";
        internal const string _ownerId = "ChaCardSupport";
        #endregion

#if IPA
        public override string Name { get { return _name; } }
        public override string Version { get { return _version; } }
        public override string[] Filter { get { return new[] { "StudioNEO_32", "StudioNEO_64" }; } }
#endif

        #region Private Types
        #endregion

        #region Private Variables

        internal static new ManualLogSource Logger;
        internal static ChaCardSupport _self;

        private static string _assemblyLocation;
        private bool _loaded = false;

        private AssetBundle _bundle;
        #endregion

        #region Accessors
        // internal static ConfigEntry<bool> CaptureFull { get; private set; }
        internal static ConfigEntry<int> SaveScale { get; private set; }

        #endregion

        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();

            _self = this;

            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var harmonyInstance = HarmonyExtensions.CreateInstance(GUID);
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

            // CaptureFull = Config.Bind("Option", "Capture", true, "Capture FullSize"); 
            SaveScale = Config.Bind("Option", "Scale", 1, new ConfigDescription("Card Width|Height Scale", new AcceptableValueRange<int>(1, 3)));
        }

#if SUNSHINE || HONEYSELECT2 || AISHOUJO
        protected override void LevelLoaded(Scene scene, LoadSceneMode mode)
        {
            base.LevelLoaded(scene, mode);
            if (mode == LoadSceneMode.Single && scene.buildIndex == 2)
                Init();
        }
#endif        
        protected override void Update()
        {
            if (_loaded == false)
                return;
        }
        #endregion

        #region Private Methods
        private void Init()
        {
            _loaded = true;
        }        

        #endregion

        #region Public Methods        
        #endregion

        #region Patches
        [HarmonyPatch(typeof(PngAssist), "SavePng",  typeof(BinaryWriter), typeof(int), typeof(int), typeof(int), typeof(int), typeof(float), typeof(bool), typeof(bool))]
        private static class PngAssist_SavePng_Patches
        {
            private static bool Prefix(PngAssist __instance, BinaryWriter writer, int capW = 504, int capH = 704, int createW = 252, int createH = 352, float renderRate = 1f, bool drawBackSp = true, bool drawFrontSp = true)
            {                
                UnityEngine.Debug.Log($">> SavePng in PngAssist");

                byte[] array = null;

                if (createW == 252 && createH == 352) {
                    createW = _self.SaveScale * createW;
                    createH = _self.SaveScale * createH;

                    if (_self.CaptureFull) {
                        Vector2 screenSize = ScreenInfo.GetScreenSize();
                        capW = (int)screeSize.x;
                        capH = (int)screeSize.y;
                    }

                    __instance.CreatePng(ref array, capW, capH, createW, createH, renderRate, drawBackSp, drawFrontSp);
                    if (array == null)
                    {
                        return true;
                    }
                    writer.Write(array);
                    array = null;                

                    Logger.LogMessage($"Capture Vis ChaCardSupport w:{createW}, h:{createH}");            
                    return false;
                }

                return true;
            }
        }
        
        [HarmonyPatch(typeof(Studio.GameScreenShot), "CreatePngScreen", typeof(int), typeof(int), typeof(bool), typeof(bool))]
        private static class GameScreenShot_CreatePngScreen_Patches
        {
            // 320, 180,
            private static bool Prefix(Studio.GameScreenShot __instance, int _width, int _height, bool _ARGB = false, bool _cap = false)
            {                
                UnityEngine.Debug.Log($">> CreatePngScreen in GameScreenShot");

//          Texture2D texture2D = new Texture2D(_width, _height, _ARGB ? TextureFormat.ARGB32 : TextureFormat.RGB24, false);
// 			int antiAliasing = (QualitySettings.antiAliasing == 0) ? 1 : QualitySettings.antiAliasing;
// 			RenderTexture temporary = RenderTexture.GetTemporary(texture2D.width, texture2D.height, 24, RenderTextureFormat.Default, RenderTextureReadWrite.Default, antiAliasing);
// 			if (_cap)
// 			{
// 				this.imageCap.enabled = true;
// 			}
// 			Graphics.SetRenderTarget(temporary);
// 			GL.Clear(true, true, Color.black);
// 			Graphics.SetRenderTarget(null);
// 			bool sRGBWrite = GL.sRGBWrite;
// 			GL.sRGBWrite = true;
// 			foreach (Camera camera in this.renderCam)
// 			{
// 				if (!(null == camera))
// 				{
// 					int cullingMask = camera.cullingMask;
// 					camera.cullingMask &= ~(1 << LayerMask.NameToLayer("Studio/Camera"));
// 					bool enabled = camera.enabled;
// 					RenderTexture targetTexture = camera.targetTexture;
// 					Rect rect = camera.rect;
// 					camera.enabled = true;
// 					camera.targetTexture = temporary;
// 					camera.Render();
// 					camera.targetTexture = targetTexture;
// 					camera.rect = rect;
// 					camera.enabled = enabled;
// 					camera.cullingMask = cullingMask;
// 				}
// 			}
// 			if (_cap)
// 			{
// 				this.imageCap.enabled = false;
// 			}
// 			GL.sRGBWrite = sRGBWrite;
// 			RenderTexture.active = temporary;
// 			texture2D.ReadPixels(new Rect(0f, 0f, (float)texture2D.width, (float)texture2D.height), 0, 0);
// 			texture2D.Apply();
// 			RenderTexture.active = null;
// 			byte[] result = texture2D.EncodeToPNG();
// 			RenderTexture.ReleaseTemporary(temporary);
// 			UnityEngine.Object.Destroy(texture2D);
// 			UnityEngine.Resources.UnloadUnusedAssets();
// 			return result;

                return true;
            }
        }   

        [HarmonyPatch(typeof(CharaCustom.CustomCapture), "CapCharaCard", typeof(bool), typeof(SaveFrameAssist), typeof(bool))]
        private static class CustomCapture_CapCharaCard_Patches
        {
            private static bool Prefix(CustomCapture __instance, bool enableBG, SaveFrameAssist saveFrameAssist, bool forceHideBackFrame = false)
            {                
                UnityEngine.Debug.Log($">> CapCharaCard in CustomCapture");

                // byte[] result = null;
                // bool flag = !(null == saveFrameAssist) && saveFrameAssist.backFrameDraw;
                // if (forceHideBackFrame)
                // {
                //     flag = false;
                // }
                // bool flag2 = !(null == saveFrameAssist) && saveFrameAssist.frontFrameDraw;
                // Camera camera = (null == saveFrameAssist) ? null : (flag ? saveFrameAssist.backFrameCam : null);
                // Camera camFrontFrame = (null == saveFrameAssist) ? null : (flag2 ? saveFrameAssist.frontFrameCam : null);
                // CustomCapture.CreatePng(ref result, enableBG ? this.camBG : null, camera, this.camMain, camFrontFrame);
                // camera = ((null != saveFrameAssist) ? saveFrameAssist.backFrameCam : null);
                // if (null != camera)
                // {
                //     camera.targetTexture = null;
                // }
                // return result;

                return true;
            }
        }   

        [HarmonyPatch(typeof(CharaCustom.CustomCapture), "CapCoordinateCard", typeof(bool), typeof(SaveFrameAssist), typeof(Camera))]
        private static class CustomCapture_CapCoordinateCard_Patches
        {
            private static bool Prefix(CustomCapture __instance, bool enableBG, SaveFrameAssist saveFrameAssist, Camera main)
            {                
                UnityEngine.Debug.Log($">> CapCoordinateCard in CustomCapture");

                // byte[] result = null;
                // bool flag = !(null == saveFrameAssist) && saveFrameAssist.backFrameDraw;
                // bool flag2 = !(null == saveFrameAssist) && saveFrameAssist.frontFrameDraw;
                // Camera camera = (null == saveFrameAssist) ? null : (flag ? saveFrameAssist.backFrameCam : null);
                // Camera camFrontFrame = (null == saveFrameAssist) ? null : (flag2 ? saveFrameAssist.frontFrameCam : null);
                // CustomCapture.CreatePng(ref result, enableBG ? this.camBG : null, camera, main, camFrontFrame);
                // camera = ((null != saveFrameAssist) ? saveFrameAssist.backFrameCam : null);
                // if (null != camera)
                // {
                //     camera.targetTexture = null;
                // }
                // if (null != this.camMain)
                // {
                //     this.camMain.targetTexture = null;
                // }
                // if (null != this.camBG)
                // {
                //     this.camBG.targetTexture = null;
                // }
                // return result;

                return true;
            }
        }       

        [HarmonyPatch(typeof(CharaCustom.CustomCapture), "CreatePng", typeof(byte[]), typeof(Camera), typeof(Camera), typeof(Camera), typeof(Camera))]
        private static class CustomCapture_CreatePng_Patches
        {
            private static bool Prefix(CustomCapture __instance, ref byte[] pngData, Camera _camBG = null, Camera _camBackFrame = null, Camera _camMain = null, Camera _camFrontFrame = null)
            {                
                UnityEngine.Debug.Log($">> CreatePng in CustomCapture");

	            // int num = 1280;
                // int num2 = 720;
                // int num3 = 504;
                // int num4 = 704;
                // RenderTexture temporary;
                // if (QualitySettings.antiAliasing == 0)
                // {
                //     temporary = RenderTexture.GetTemporary(num, num2, 24);
                // }
                // else
                // {
                //     temporary = RenderTexture.GetTemporary(num, num2, 24, RenderTextureFormat.Default, RenderTextureReadWrite.Default, QualitySettings.antiAliasing);
                // }
                // bool sRGBWrite = GL.sRGBWrite;
                // GL.sRGBWrite = true;
                // if (null != _camMain)
                // {
                //     RenderTexture targetTexture = _camMain.targetTexture;
                //     bool allowHDR = _camMain.allowHDR;
                //     _camMain.allowHDR = false;
                //     _camMain.targetTexture = temporary;
                //     _camMain.Render();
                //     _camMain.targetTexture = targetTexture;
                //     _camMain.allowHDR = allowHDR;
                // }
                // if (null != _camBG)
                // {
                //     bool allowHDR2 = _camBG.allowHDR;
                //     _camBG.allowHDR = false;
                //     _camBG.targetTexture = temporary;
                //     _camBG.Render();
                //     _camBG.targetTexture = null;
                //     _camBG.allowHDR = allowHDR2;
                // }
                // if (null != _camBackFrame)
                // {
                //     _camBackFrame.targetTexture = temporary;
                //     _camBackFrame.Render();
                //     _camBackFrame.targetTexture = null;
                // }
                // if (null != _camFrontFrame)
                // {
                //     _camFrontFrame.targetTexture = temporary;
                //     _camFrontFrame.Render();
                //     _camFrontFrame.targetTexture = null;
                // }
                // GL.sRGBWrite = sRGBWrite;
                // Texture2D texture2D = new Texture2D(num3, num4, TextureFormat.RGB24, false, true);
                // RenderTexture.active = temporary;
                // texture2D.ReadPixels(new Rect((float)(num - num3) / 2f, (float)(num2 - num4) / 2f, (float)num3, (float)num4), 0, 0);
                // texture2D.Apply();
                // RenderTexture.active = null;
                // RenderTexture.ReleaseTemporary(temporary);
                // TextureScale.Bilinear(texture2D, num3 / 2, num4 / 2);
                // pngData = texture2D.EncodeToPNG();
                // UnityEngine.Object.Destroy(texture2D);

                return true;
            }
        }   
        #endregion
    }
}