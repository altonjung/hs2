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


namespace InGameOverhaul
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
    public class InGameOverhaul : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "InGameOverhaul";
        public const string Version = "0.9.0.0";
        public const string GUID = "com.alton.illusionplugins.RealHuman";
        internal const string _ownerId = "InGameOverhaul";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "InGameOverhaul";
#endif
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
        internal static InGameOverhaul _self;

        private static string _assemblyLocation;
        private bool _loaded = false;

        private AssetBundle _bundle;


        #region Accessors
        internal static ConfigEntry<KeyboardShortcut> ConfigMainWindowShortcut { get; private set; }
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
        }

#if HONEYSELECT
        protected override void LevelLoaded(int level)
        {
            if (level == 3)
                this.Init();
        }
#elif SUNSHINE || HONEYSELECT2 || AISHOUJO
        protected override void LevelLoaded(Scene scene, LoadSceneMode mode)
        {
            base.LevelLoaded(scene, mode);
            if (mode == LoadSceneMode.Single && scene.buildIndex == 2)
                Init();
        }

#elif KOIKATSU
        protected override void LevelLoaded(Scene scene, LoadSceneMode mode)
        {
            base.LevelLoaded(scene, mode);
            if (mode == LoadSceneMode.Single && scene.buildIndex == 1)
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

        private void SceneInit()
        {
            // UnityEngine.Debug.Log($">> SceneInit()");
        }

        #endregion

        #region Public Methods
        public void OnVideoEnd() {

        }
        #endregion

        #region Patches

        private static void PlayVideo() {
           string videoPath = @"C:\temp\test.mp4"; // 재생할 파일 경로

           GameObject videoObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
           videoObj.transform.position = new Vector3(0, 0, 3); // 카메라 앞

           VideoPlayer videoPlayer = videoObj.AddComponent<VideoPlayer>();

           // 비디오 파일 경로 설정
           videoPlayer.source = VideoSource.Url;
           videoPlayer.url = videoPath;

           // 재생 옵션
           videoPlayer.playOnAwake = false;
           videoPlayer.isLooping = false; // 반복 안 함

           // 영상 출력: 현재 오브젝트의 MeshRenderer에 표시
           videoPlayer.renderMode = VideoRenderMode.MaterialOverride;
           videoPlayer.targetMaterialRenderer = videoObj.GetComponent<Renderer>();
           videoPlayer.targetMaterialProperty = "_MainTex";

           // 재생 종료 이벤트 등록
           videoPlayer.loopPointReached += OnVideoEnd;

           // update 함수에서
           // if (Input.GetKeyDown(KeyCode.Escape))
           // {
           //     Debug.Log("Video Stop");
           //     videoPlayer.Stop();
           // }
        }

        [HarmonyPatch(typeof(Manager.FurRoomSceneManager), "LoadConciergeBody")]
        private static class FurRoomSceneManager_LoadConciergeBody_Patches
        {
            private static void Postfix(Manager.FurRoomSceneManager __instance)
            {                
                UnityEngine.Debug.Log($">> LoadConciergeBody in FurRoom");          
            }
        }

        [HarmonyPatch(typeof(Manager.LobbySceneManager), "LoadConciergeBody")]
        private static class LobbySceneManager_LoadConciergeBody_Patches
        {
            private static void Postfix(Manager.LobbySceneManager __instance)
            {                
                UnityEngine.Debug.Log($">> LoadConciergeBody in Lobby");
            }
        }

        [HarmonyPatch(typeof(Manager.HomeSceneManager), "LoadConciergeBody")]
        private static class HomeSceneManager_LoadConciergeBody_Patches
        {
            private static void Postfix(Manager.HomeSceneManager __instance)
            {                
                UnityEngine.Debug.Log($">> LoadConciergeBody in Home");
            }
        }

        [HarmonyPatch(typeof(Manager.SpecialTreatmentRoomManager), "LoadConciergeBody")]
        private static class SpecialTreatmentRoomManager_LoadConciergeBody_Patches
        {
            private static void Postfix(Manager.SpecialTreatmentRoomManager __instance)
            {                
                UnityEngine.Debug.Log($">> LoadConciergeBody in RoomManager");          
            }
        }        

        [HarmonyPatch(typeof(Manager.HSceneManager), "SetFemaleState", typeof(ChaControl[]))]
        private static class HSceneManager_SetFemaleState_Patches
        {
            private static void Postfix(Manager.HSceneManager __instance, ChaControl[] female)
            {
                // player
                //__instance.player;
            }
        }

        [HarmonyPatch(typeof(HScene), "Start")]
        private static class HScene_ChangeCoodinate_Patches
        {
            private static bool Prefix(HScene __instance)
            {
                UnityEngine.Debug.Log($">> Start in HScene");
                return true;
            }
        }

        // [HarmonyPatch(typeof(GlobalMethod), "setCameraMoveFlag", typeof(HScene.CameraControl_Ver2), typeof(bool))]
        // private static class GlobalMethod_setCameraMoveFlag_Patches
        // {
        //     private static bool Prefix(GlobalMethod __instance, CameraControl_Ver2 _ctrl, bool _bPlay)
        //     {
        //         UnityEngine.Debug.Log($">> setCameraMoveFlag in GlobalMethod {_bPlay}");
        //         return true;
        //     }
        // }

        [HarmonyPatch(typeof(HScene), "StartAnim", typeof(HScene.AnimationListInfo))]
        private static class HScene_StartAnim_Patches
        {
            private static bool Prefix(HScene __instance, HScene.AnimationListInfo _info)
            {
                UnityEngine.Debug.Log($">> StartAnim in HScene eventNo:  {_info.nameAnimation} {Singleton<Manager.Game>.Instance.eventNo}, peek: {Singleton<Manager.Game>.Instance.peepKind}");
                return true;        
            }
        }

        [HarmonyPatch(typeof(HSceneSprite), "OnClickFinish")]
        private static class HSceneSprite_OnClickFinish_Patches
        {
            private static bool Prefix(HSceneSprite __instance)
            {
                UnityEngine.Debug.Log($">> OnClickFinish in HScene eventNo: {Singleton<Manager.Game>.Instance.eventNo}, peek: {Singleton<Manager.Game>.Instance.peepKind}");
                return true;        
            }
        }

        [HarmonyPatch(typeof(HSceneSprite), "OnClickFinishInSide")]
        private static class HSceneSprite_OnClickFinishInSide_Patches
        {
            private static bool Prefix(HSceneSprite __instance)
            {
                UnityEngine.Debug.Log($">> OnClickFinishInSide in HScene eventNo: {Singleton<Manager.Game>.Instance.eventNo}, peek: {Singleton<Manager.Game>.Instance.peepKind}");
                return true;        
            }
        }

        [HarmonyPatch(typeof(HSceneSprite), "OnClickFinishOutSide")]
        private static class HSceneSprite_OnClickFinishOutSide_Patches
        {
            private static bool Prefix(HSceneSprite __instance)
            {
                UnityEngine.Debug.Log($">> OnClickFinishOutSide in HScene eventNo: {Singleton<Manager.Game>.Instance.eventNo}, peek: {Singleton<Manager.Game>.Instance.peepKind}");
                return true;        
            }
        }                

        [HarmonyPatch(typeof(HSceneSprite), "OnClickFinishDrink")]
        private static class HSceneSprite_OnClickFinishDrink_Patches
        {
            private static bool Prefix(HSceneSprite __instance)
            {
                UnityEngine.Debug.Log($">> OnClickFinishDrink in HScene eventNo: {Singleton<Manager.Game>.Instance.eventNo}, peek: {Singleton<Manager.Game>.Instance.peepKind}");
                return true;        
            }
        }

        [HarmonyPatch(typeof(HSceneSprite), "OnClickSpanking")]
        private static class HSceneSprite_OnClickSpanking_Patches
        {
            private static bool Prefix(HSceneSprite __instance)
            {
                UnityEngine.Debug.Log($">> OnClickSpanking in HScene eventNo: {Singleton<Manager.Game>.Instance.eventNo}, peek: {Singleton<Manager.Game>.Instance.peepKind}");
                return true;        
            }
        }

        [HarmonyPatch(typeof(HSceneSprite), "OnClickSceneEnd")]
        private static class HSceneSprite_OnClickSceneEnd_Patches
        {
            private static bool Prefix(HSceneSprite __instance)
            {
                UnityEngine.Debug.Log($">> OnClickSceneEnd in HScene eventNo: {Singleton<Manager.Game>.Instance.eventNo}, peek: {Singleton<Manager.Game>.Instance.peepKind}");
                return true;        
            }
        }

        [HarmonyPatch(typeof(HSceneSprite), "OnClickCloth", typeof(int))]
        private static class HSceneSprite_OnClickCloth_Patches
        {
            // 너무 심하게 굴면.. this.ctrlFlag.click = HSceneFlagCtrl.ClickKind.LeaveItToYou;
            private static bool Prefix(HSceneSprite __instance, int mode)
            {
                // mode = 1 -> cloth
                // mode = 2 -> accessory
                UnityEngine.Debug.Log($">> OnClickCloth {mode} in HScene");
                return true;        
            }
        }

        [HarmonyPatch(typeof(HSceneSprite), "OnClickMotion", typeof(int))]
        private static class HSceneSprite_OnClickMotion_Patches
        {
            private static bool Prefix(HSceneSprite __instance, int _motion)
            {
                UnityEngine.Debug.Log($">> OnClickMotion {_motion} in HScene");
                return true;        
            }
        }        

        [HarmonyPatch(typeof(HSceneSpriteClothCondition), "SetClothCharacter", typeof(bool))]
        private static class HSceneSpriteClothCondition_SetClothCharacter_Patches
        {
            private static bool Prefix(HSceneSpriteClothCondition __instance, bool init)
            {
                // mode = 1 -> cloth
                // mode = 2 -> accessory
                UnityEngine.Debug.Log($">> SetClothCharacter {init} in HScene");
                return true;        
            }
        }

        [HarmonyPatch(typeof(HVoiceCtrl), "SetClothCharacter", typeof(bool))]
        private static class HVoiceCtrl_SetClothCharacter_Patches
        {
            private static bool Prefix(HVoiceCtrl __instance, bool init)
            {
                // mode = 1 -> cloth
                // mode = 2 -> accessory
                UnityEngine.Debug.Log($">> SetClothCharacter {init} in HScene");
                return true;        
            }
        }

        [HarmonyPatch(typeof(Manager.Sound), "Play", typeof(Manager.Voice.Loader), typeof(Action<AudioSource>))]
        private static class Sound_Play_Patches
        {
           private static bool Prefix(Manager.Sound __instance, Manager.Voice.Loader loader, Action<AudioSource> action)
           {
               UnityEngine.Debug.Log($">> Play Sound {loader.bundle}, {loader.asset} in HScene");
               return true;        
           }
        }

        [HarmonyPatch(typeof(Manager.Sound), "PlayAsync", typeof(Manager.Voice.Loader), typeof(Action<AudioSource>))]
        private static class Sound_PlayAsync_Patches
        {
           private static bool Prefix(Manager.Sound __instance, Manager.Voice.Loader loader, Action<AudioSource> action)
           {
               UnityEngine.Debug.Log($">> PlayAsync Sound {loader.bundle}, {loader.asset} in HScene");
               return true;        
           }
        }

        [HarmonyPatch(typeof(Manager.Voice), "Play", typeof(Manager.Voice.Loader), typeof(Action<AudioSource>))]
        private static class Voice_Play_Patches
        {
           private static bool Prefix(Manager.Voice __instance, Manager.Voice.Loader loader, Action<AudioSource> action)
           {
               UnityEngine.Debug.Log($">> Play Voice {loader.bundle}, {loader.asset} in HScene");
               return true;        
           }
        }

        [HarmonyPatch(typeof(Manager.Voice), "OncePlay", typeof(Manager.Voice.Loader), typeof(Action<AudioSource>))]
        private static class Voice_OncePlay_Patches
        {
           private static bool Prefix(Manager.Voice __instance, Manager.Voice.Loader loader, Action<AudioSource> action)
           {
               UnityEngine.Debug.Log($">> OncePlay Voice {loader.bundle}, {loader.asset} in HScene");
               return true;        
           }
        }

        [HarmonyPatch(typeof(Manager.Voice), "OncePlayChara", typeof(Manager.Voice.Loader), typeof(Action<AudioSource>))]
        private static class Voice_OncePlayChara_Patches
        {
           private static bool Prefix(Manager.Voice __instance, Manager.Voice.Loader loader, Action<AudioSource> action)
           {
               UnityEngine.Debug.Log($">> OncePlayChara Voice {loader.bundle}, {loader.asset} in HScene");
               return true;        
           }
        }

        [HarmonyPatch(typeof(HVoiceCtrl), "LoadShortBreath", typeof(int), typeof(int))]
        private static class HVoiceCtrl_LoadShortBreath_Patches
        {
            private static bool Prefix(HVoiceCtrl __instance, int _personality, int _main)
            {
                UnityEngine.Debug.Log($">> LoadShortBreath HVoiceCtrl {_personality}, {_main}, text {GlobalMethod.LoadAllListText(__instance.lstBreathAbnames, __instance.sbLoadFile.ToString(), false)} in HScene");
                return true;
            }
        }

        [HarmonyPatch(typeof(ChaControl), "ChangeNowCoordinate", typeof(string), typeof(bool), typeof(bool), typeof(bool))]
        private static class ChaControl_ChangeCoodinate_Patches
        {
            private static void Postfix(ChaControl __instance, string path, bool reload, bool forceChange)
            {

            }
        }

        // // 악세러리 부분 변경
        // [HarmonyPatch(typeof(ChaControl), "ChangeAccessory", typeof(int), typeof(int), typeof(int), typeof(string), typeof(bool))]
        // private static class ChaControl_ChangeAccessory_Patches
        // {
        //     private static void Postfix(ChaControl __instance, int slotNo, int type, int id, string parentKey, bool forceChange)
        //     {
        //         OCIChar ociChar = __instance.GetOCIChar() as OCIChar;
        //         if (ociChar != null)
        //         {
        //             if (_self._ociCharMgmt.TryGetValue(ociChar.GetHashCode(), out var realHumanData))
        //             {
        //                 ociChar.charInfo.StartCoroutine(ExecuteAfterFrame(ociChar, realHumanData));
        //             }
        //         }
        //     }
        // }

        // // 옷 부분 변경
        // [HarmonyPatch(typeof(ChaControl), "ChangeClothes", typeof(int), typeof(int), typeof(bool))]
        // private static class ChaControl_ChangeClothes_Patches
        // {
        //     private static void Postfix(ChaControl __instance, int kind, int id, bool forceChange)
        //     {
        //         OCIChar ociChar = __instance.GetOCIChar() as OCIChar;
        //         if (ociChar != null)
        //         {
        //             if (_self._ociCharMgmt.TryGetValue(ociChar.GetHashCode(), out var realHumanData))
        //             {
        //                 ociChar.charInfo.StartCoroutine(ExecuteAfterFrame(ociChar, realHumanData));
        //             }
        //         }
        //     }
        // }

        // // 옷 전체 변경
        // [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.SetAccessoryStateAll), typeof(bool))]
        // internal static class ChaControl_SetAccessoryStateAll_Patches
        // {
        //     public static void Postfix(ChaControl __instance, bool show)
        //     {
        //         OCIChar ociChar = __instance.GetOCIChar() as OCIChar;
        //         if (ociChar != null)
        //         {
        //             if (_self._ociCharMgmt.TryGetValue(ociChar.GetHashCode(), out var realHumanData))
        //             {
        //                 ociChar.charInfo.StartCoroutine(ExecuteAfterFrame(ociChar, realHumanData));
        //             }
        //         }
        //     }
        // }
        #endregion
    }

    #endregion
}