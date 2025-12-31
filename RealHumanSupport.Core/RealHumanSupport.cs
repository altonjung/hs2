﻿using Studio;
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
using UILib.ContextMenu;
using UILib.EventHandlers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using KK_PregnancyPlus;
using System.Threading.Tasks;
using System.Numerics;



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
using ADV.Commands.Camera;
using KKAPI.Studio;
using IllusionUtility.GetUtility;
using ADV.Commands.Object;
#endif
using RootMotion.FinalIK;

#if AISHOUJO || HONEYSELECT2
using AIChara;
#endif


namespace RealHumanSupport
{

#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if KOIKATSU || SUNSHINE
    [BepInProcess("CharaStudio")]
#elif AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
    [BepInProcess("HoneySelect2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class RealHumanSupport : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "RealHumanSupport";
        public const string Version = "0.9.0.5";
        public const string GUID = "com.alton.illusionplugins.RealHuman";
        internal const string _ownerId = "RealHumanSupport";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "RealHuman_support";
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
        internal static RealHumanSupport _self;

        private static string _assemblyLocation;
        internal bool _loaded = false;

        internal bool _isStudio = false;

        internal OCIChar _selectedOciChar;

        private AssetBundle _bundle;

        internal Texture2D _faceExpressionFemaleBumpMap2;

        internal Texture2D _faceExpressionMaleBumpMap2;

        internal Texture2D _bodyStrongFemale_A_BumpMap2;

        internal Texture2D _bodyStrongFemale_B_BumpMap2;

        internal Texture2D _bodyStrongMale_A_BumpMap2;

        internal Texture2D _bodyStrongMale_B_BumpMap2;

        internal ComputeShader _mergeComputeShader;

        internal int _mouth_type;
        internal int _eye_type;

        internal RenderTexture _head_rt;

        internal ComputeBuffer _head_areaBuffer;

#if FEATURE_DYNAMIC_POSITION_CHANGE_SUPPORT
        private bool _controlKosi = false;
#endif
        internal RenderTexture _body_rt;
        internal ComputeBuffer _body_areaBuffer;
        internal Dictionary<int, RealHumanData> _ociCharMgmt = new Dictionary<int, RealHumanData>();
        internal Dictionary<int, RealFaceData> _faceMouthMgmt = new Dictionary<int, RealFaceData>();
        internal Dictionary<int, RealFaceData> _faceEyesMgmt = new Dictionary<int, RealFaceData>();
        internal Coroutine _CheckRotationRoutine;

        // Config
        internal static ConfigEntry<bool> EyeShakeActive { get; private set; }

        internal static ConfigEntry<bool> ExBoneColliderActive { get; private set; }

        internal static ConfigEntry<bool> BreathActive { get; private set; }

        internal static ConfigEntry<bool> FaceBumpActive { get; private set; }

        internal static ConfigEntry<bool> BodyBumpActive { get; private set; }


        // spine_scale 
        internal static ConfigEntry<float> ExtraBoneScale{ get; private set; }

        internal static ConfigEntry<bool> ExtraBoneDebug{ get; private set; }

        internal static ConfigEntry<float> BreathStrong { get; private set; }

        internal static ConfigEntry<float> BreathInterval { get; private set; }

        #region Accessors
        internal static ConfigEntry<KeyboardShortcut> ConfigMainWindowShortcut { get; private set; }
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();

            EyeShakeActive = Config.Bind("Enable", "Eye shaking", true, new ConfigDescription("Enable/Disable"));

            ExBoneColliderActive = Config.Bind("Enable", "Extra dynamic Bone", true, new ConfigDescription("Enable/Disable"));

            BreathActive = Config.Bind("Enable", "Bumping belly", true, new ConfigDescription("Enable/Disable"));

            FaceBumpActive = Config.Bind("Enable", "Bumping face", true, new ConfigDescription("Enable/Disable"));

            BodyBumpActive = Config.Bind("Enable", "Bumping body", true, new ConfigDescription("Enable/Disable"));

            BreathInterval = Config.Bind("Breath", "Cycle", 1.5f, new ConfigDescription("Breath Interval", new AcceptableValueRange<float>(1.0f,  5.0f)));;

            BreathStrong = Config.Bind("Breath", "Strong", 0.5f, new ConfigDescription("Breath Amplitude", new AcceptableValueRange<float>(0.1f, 1.0f)));

            ExtraBoneScale = Config.Bind("ExtraCollider", "Scale", 1.0f, new ConfigDescription("Extra collider Scale", new AcceptableValueRange<float>(0.1f, 10.0f)));

            ExtraBoneDebug = Config.Bind("ExtraCollider", "Show", false, new ConfigDescription("Debug Enable/Disable"));
  
            _self = this;

            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var harmonyInstance = HarmonyExtensions.CreateInstance(GUID);
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

            // UnityEngine.Debug.Log($">> start CheckRotationRoutine");

            _CheckRotationRoutine = StartCoroutine(CheckRotationRoutine());
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
#if FEATURE_DYNAMIC_POSITION_CHANGE_SUPPORT   
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit))
            {
                // 팔에 해당하는 콜라이더 클릭 시 제어 시작
                if (hit.transform.name.Contains("RHTriggerCapsuleObj"))
                {
                    // UnityEngine.Debug.Log($">> hit Trigger");
                    _controlKosi = true;
                }
            }
        }
        if (Input.GetMouseButtonUp(0))
        {
            _controlKosi = false;
        }

        protected override void LateUpdate()
        {
            if (_loaded == false)
                return;
        }
#endif
        }
        #endregion

        #region Private Methods
        private void Init()
        {
            _loaded = true;

            if (Studio.Studio.instance != null)
            {
                _isStudio = true;

                UIUtility.Init();
                
                // 배포용 번들 파일 경로
                string bundlePath = Application.dataPath + "/../abdata/realgirl/realgirlbundle.unity3d";

                _bundle = AssetBundle.LoadFromFile(bundlePath);
                if (_bundle == null)
                {                    
                    Logger.LogMessage($"Please Install realgirl.zipmod!");
                    return;
                }

                _bodyStrongFemale_A_BumpMap2 = _bundle.LoadAsset<Texture2D>("Body_Strong_F_BumpMap2");
                _bodyStrongFemale_B_BumpMap2 = _bundle.LoadAsset<Texture2D>("Body_Strong_FB_BumpMap2");
                _bodyStrongMale_A_BumpMap2 = _bundle.LoadAsset<Texture2D>("Body_Strong_M_BumpMap2");
                _bodyStrongMale_B_BumpMap2 = _bundle.LoadAsset<Texture2D>("Body_Strong_MB_BumpMap2");
                
                _faceExpressionFemaleBumpMap2 = _bundle.LoadAsset<Texture2D>("Face_Expression_F_BumpMap2");
                _faceExpressionMaleBumpMap2 = _bundle.LoadAsset<Texture2D>("Face_Expression_M_BumpMap2");

                _mergeComputeShader = _bundle.LoadAsset<ComputeShader>("MergeTextures.compute");

                // UnityEngine.Debug.Log($">> _mergeComputeShader {_mergeComputeShader}");
                
                _faceMouthMgmt.Add(0, new RealFaceData());
                _faceMouthMgmt.Add(1, new RealFaceData(Logic.InitBArea(512, 620, 120, 80, 0.6f)));
                _faceMouthMgmt.Add(2, new RealFaceData(Logic.InitBArea(512, 640, 120, 100, 0.7f)));
                _faceMouthMgmt.Add(3, new RealFaceData());
                _faceMouthMgmt.Add(4, new RealFaceData(Logic.InitBArea(512, 690, 70, 75, 0.6f)));
                _faceMouthMgmt.Add(5, new RealFaceData(Logic.InitBArea(512, 690, 70, 75, 0.6f)));
                _faceMouthMgmt.Add(6, new RealFaceData(Logic.InitBArea(512, 690, 70, 75, 0.6f)));
                _faceMouthMgmt.Add(7, new RealFaceData(Logic.InitBArea(470, 590, 50, 50)));
                _faceMouthMgmt.Add(8, new RealFaceData(Logic.InitBArea(560, 590, 50, 50)));
                _faceMouthMgmt.Add(9, new RealFaceData(Logic.InitBArea(512, 590, 100, 60)));
                _faceMouthMgmt.Add(10, new RealFaceData(Logic.InitBArea(512, 630, 40, 40), Logic.InitBArea(330, 650, 110, 130), Logic.InitBArea(700, 650, 110, 130)));
                _faceMouthMgmt.Add(11, new RealFaceData(Logic.InitBArea(512, 630, 40, 40), Logic.InitBArea(330, 650, 110, 130), Logic.InitBArea(700, 650, 110, 130)));
                _faceMouthMgmt.Add(12, new RealFaceData(Logic.InitBArea(512, 690, 90, 60, 0.5f)));
                _faceMouthMgmt.Add(13, new RealFaceData(Logic.InitBArea(330, 650, 110, 130), Logic.InitBArea(700, 650, 110, 130)));
                _faceMouthMgmt.Add(14, new RealFaceData());
                _faceMouthMgmt.Add(15, new RealFaceData(Logic.InitBArea(512, 690, 90, 60, 0.6f)));
                _faceMouthMgmt.Add(16, new RealFaceData(Logic.InitBArea(512, 690, 90, 60, 0.6f)));
                _faceMouthMgmt.Add(17, new RealFaceData(Logic.InitBArea(330, 650, 110, 130), Logic.InitBArea(700, 650, 110, 130)));
                _faceMouthMgmt.Add(18, new RealFaceData(Logic.InitBArea(330, 650, 110, 130), Logic.InitBArea(700, 650, 110, 130)));
                _faceMouthMgmt.Add(19, new RealFaceData());
                _faceMouthMgmt.Add(20, new RealFaceData());
                _faceMouthMgmt.Add(21, new RealFaceData(Logic.InitBArea(512, 690, 90, 60, 0.6f)));
                _faceMouthMgmt.Add(22, new RealFaceData());
                _faceMouthMgmt.Add(23, new RealFaceData(Logic.InitBArea(512, 690, 90, 60, 0.6f)));
                _faceMouthMgmt.Add(24, new RealFaceData(Logic.InitBArea(512, 690, 90, 60, 0.6f)));
                _faceMouthMgmt.Add(25, new RealFaceData());

                _faceEyesMgmt.Add(0, new RealFaceData()); //
                _faceEyesMgmt.Add(1, new RealFaceData(Logic.InitBArea(300, 490, 60, 60, 0.6f), Logic.InitBArea(720, 490, 60, 60, 0.6f), Logic.InitBArea(435, 505, 40, 40, 0.8f), Logic.InitBArea(585, 505, 40, 40, 0.8f))); //
                _faceEyesMgmt.Add(2, new RealFaceData(Logic.InitBArea(300, 490, 60, 60, 0.8f), Logic.InitBArea(720,490, 60, 60, 0.8f), Logic.InitBArea(435, 505, 40, 40, 0.8f), Logic.InitBArea(585, 505, 40, 40, 0.8f))); //
                _faceEyesMgmt.Add(3, new RealFaceData());
                _faceEyesMgmt.Add(4, new RealFaceData());
                _faceEyesMgmt.Add(5, new RealFaceData());
                _faceEyesMgmt.Add(6, new RealFaceData());
                _faceEyesMgmt.Add(7, new RealFaceData(Logic.InitBArea(300, 490, 60, 60, 0.6f), Logic.InitBArea(720, 490, 60, 60, 0.6f), Logic.InitBArea(435, 505, 40, 40, 1.3f), Logic.InitBArea(585, 505, 40, 40, 1.3f))); //
                _faceEyesMgmt.Add(8, new RealFaceData(Logic.InitBArea(300, 490, 60, 60), Logic.InitBArea(435, 505, 40, 40, 0.8f), Logic.InitBArea(585, 505, 40, 40, 0.8f))); //
                _faceEyesMgmt.Add(9, new RealFaceData(Logic.InitBArea(720, 470, 60, 40), Logic.InitBArea(435, 505, 40, 40, 0.8f), Logic.InitBArea(585, 505, 40, 40, 0.8f))); //
                _faceEyesMgmt.Add(10, new RealFaceData());
                _faceEyesMgmt.Add(11, new RealFaceData()); //
                _faceEyesMgmt.Add(12, new RealFaceData(Logic.InitBArea(300, 490, 60, 60), Logic.InitBArea(435, 505, 40, 40, 0.8f))); //
                _faceEyesMgmt.Add(13, new RealFaceData(Logic.InitBArea(720, 490, 60, 60), Logic.InitBArea(585, 505, 40, 40, 0.8f))); //

                _self._head_areaBuffer = new ComputeBuffer(16, sizeof(float) * 6);
                _self._body_areaBuffer = new ComputeBuffer(24, sizeof(float) * 6); 
            }
        }

        private void SceneInit()
        {
            // UnityEngine.Debug.Log($">> SceneInit()");
            if (_isStudio)
            {
                foreach (var kvp in _ociCharMgmt)
                {
                    var key = kvp.Key;
                    RealHumanData value = kvp.Value;
                    value.c_m_eye.Clear();
                    if (value != null && value.ociChar != null && value.coroutine != null)
                        value.ociChar.charInfo.StopCoroutine(value.coroutine);
                }

                _mouth_type = 0;
                _eye_type = 0;

                _ociCharMgmt.Clear();                
            }
        }

        IEnumerator CheckRotationRoutine()
        {
            while (true) // 무한 반복
            {
                if (_selectedOciChar != null)
                {
                    if (!Input.GetMouseButton(0)) {
                        if (_ociCharMgmt.TryGetValue(_selectedOciChar.GetHashCode(), out var realHumanData))
                        {
                            if (realHumanData.m_skin_body == null || realHumanData.m_skin_head == null)
                                realHumanData = Logic.GetMaterials(_selectedOciChar, realHumanData);

                            if (
                                (Logic.GetBoneRotationFromIK(realHumanData.lk_left_foot_bone)._q != realHumanData.prev_lk_left_foot_rot) ||
                                (Logic.GetBoneRotationFromIK(realHumanData.lk_right_foot_bone)._q != realHumanData.prev_lk_right_foot_rot) ||
                                (Logic.GetBoneRotationFromFK(realHumanData.fk_left_foot_bone)._q != realHumanData.prev_fk_left_foot_rot) ||
                                (Logic.GetBoneRotationFromFK(realHumanData.fk_right_foot_bone)._q != realHumanData.prev_fk_right_foot_rot) ||
                                (Logic.GetBoneRotationFromFK(realHumanData.fk_left_knee_bone)._q != realHumanData.prev_fk_left_knee_rot) ||
                                (Logic.GetBoneRotationFromFK(realHumanData.fk_right_knee_bone)._q != realHumanData.prev_fk_right_knee_rot) ||
                                (Logic.GetBoneRotationFromFK(realHumanData.fk_left_thigh_bone)._q != realHumanData.prev_fk_left_thigh_rot) ||
                                (Logic.GetBoneRotationFromFK(realHumanData.fk_right_thigh_bone)._q != realHumanData.prev_fk_right_thigh_rot) ||                          
                                (Logic.GetBoneRotationFromFK(realHumanData.fk_spine01_bone)._q != realHumanData.prev_fk_spine01_rot) || 
                                (Logic.GetBoneRotationFromFK(realHumanData.fk_spine02_bone)._q != realHumanData.prev_fk_spine02_rot) ||
                                (Logic.GetBoneRotationFromFK(realHumanData.fk_head_bone)._q != realHumanData.prev_fk_head_rot) ||
                                (Logic.GetBoneRotationFromFK(realHumanData.fk_left_shoulder_bone)._q != realHumanData.prev_fk_left_shoulder_rot) ||
                                (Logic.GetBoneRotationFromFK(realHumanData.fk_right_shoulder_bone)._q != realHumanData.prev_fk_right_shoulder_rot)
                            )
                            {
                                Logic.SupportBodyBumpEffect(_selectedOciChar.charInfo, realHumanData);
                            }      
                        }                        
                    } 
                    else
                    {
#if FEATURE_FIX_LONGHAIR                        
                        if (_ociCharMgmt.TryGetValue(_selectedOciChar.GetHashCode(), out var realHumanData1))
                        {                    
                            if (realHumanData1.head_bone != null)
                            {        
                                PositionData neckData = Logic.GetBoneRotationFromTF(realHumanData1.neck_bone);
                                PositionData headData = Logic.GetBoneRotationFromTF(realHumanData1.head_bone);

                                Vector3 worldGravity = Vector3.down * 0.02f;
                                Vector3 worldForce1 = Vector3.zero;          
                                Vector3 worldForce2 = Vector3.zero;   
                                Vector3 worldForce3 = Vector3.zero;   

                                float zOffset = 0f;
                                float yOffset = 0f;
                                if (neckData._front >= 0f)
                                {   
                                    // neck이 앞으로 숙인 유형                                                                        
                                    float angle = Math.Abs(neckData._front);                                
                                    yOffset = Logic.Remap(Math.Abs(angle), 0.0f, 140.0f, 0.01f, 0.02f);
                                    zOffset = yOffset;
                                    worldForce1 = new Vector3(0, -yOffset, zOffset);
                                } else
                                {
                                    // neck이 뒤로 숙인 유형                                                                        
                                    float angle = Math.Abs(neckData._front);                                
                                    yOffset = Logic.Remap(Math.Abs(angle), 0.0f, 140.0f, 0.005f, 0.07f);
                                    zOffset = yOffset;
                                    worldForce1 = new Vector3(0, -yOffset, -zOffset);                                    
                                }

                                if (neckData._front < headData._front)
                                {
                                    // head가 앞으로 숙인 유형                                                                        
                                    float angle = Logic.GetRelativePosition(neckData._front, headData._front);                           
                                    yOffset = Logic.Remap(Math.Abs(angle), 0.0f, 120.0f, 0.01f, 0.03f);
                                    zOffset = yOffset;
                                    worldForce2 = new Vector3(0, -yOffset, zOffset);
                                } else
                                {
                                    // head가 뒤로 숙인 유형   
                                    float angle = Logic.GetRelativePosition(neckData._front, headData._front);
                                    yOffset = Logic.Remap(Math.Abs(angle), 0.0f, 120.0f, 0.005f, 0.035f);
                                    zOffset = yOffset;
                                    worldForce2 = new Vector3(0, -yOffset, -zOffset);                  
                                }

                                worldForce3 = worldForce1 + worldForce2;
                                
                                // hair 에 대해 world position 적용
                                foreach (DynamicBone hairDynamicBone in realHumanData1.hairDynamicBones)
                                {
                                    if (hairDynamicBone == null)
                                        continue;

                                    // DynamicBone 기준 로컬 변환
                                    hairDynamicBone.m_Gravity =
                                        realHumanData1.root_bone.InverseTransformDirection(worldGravity);

                                    hairDynamicBone.m_Force =
                                        realHumanData1.root_bone.InverseTransformDirection(worldForce3);
                                }                         
                            }                     
                        }     
#endif
                    }                   
                }

                yield return new WaitForSeconds(0.5f); // 0.5초 대기
            }
        }

        private IEnumerator Routine(RealHumanData realHumanData)
        {
            while (true)
            {
                if (_loaded == true)
                {
                    float time = Time.time;
                    if (EyeShakeActive.Value && _isStudio)
                    {
                        foreach (Material mat in realHumanData.c_m_eye)
                        {
                            // sin 파형 (0 ~ 1로 정규화)
                            float easedBump = (Mathf.Sin(time * Mathf.PI * 3.5f * 2f) + 1f) * 0.5f;

                            float eyeScale = Mathf.Lerp(0.18f, 0.21f, easedBump);
                            mat.SetFloat("_Texture4Rotator", eyeScale);

                            eyeScale = Mathf.Lerp(0.1f, 0.2f, easedBump);
                            mat.SetFloat("_Parallax", eyeScale);
                        }
                    }

                    if (BreathActive.Value && _isStudio)
                    {
                        float sinValue = (Mathf.Sin(time * BreathInterval.Value) + 1f) * 0.5f;
                        
                        foreach (var ctrl in StudioAPI.GetSelectedControllers<PregnancyPlusCharaController>())
                        {
                            ctrl.infConfig.SetSliders(BellyTemplate.GetTemplate(1));
                            ctrl.infConfig.inflationSize = (1f - sinValue) * 10f * BreathStrong.Value;
                            ctrl.MeshInflate(new MeshInflateFlags(ctrl), "StudioSlider");
                        }
                    }    

                    yield return null;
                }
                else
                {
                    yield return new WaitForSeconds(1);
                }
            }
        }    

        private IEnumerator ExecuteAfterFrame(OCIChar ociChar, RealHumanData realHumanData)
        {
            int frameCount = 30;
            for (int i = 0; i < frameCount; i++)
                yield return null;

            Logic.SupportExtraDynamicBones(ociChar.GetChaControl(), realHumanData);
            Logic.SupportEyeFastBlinkEffect(ociChar.GetChaControl(), realHumanData);
            Logic.SupportBodyBumpEffect(ociChar.GetChaControl(), realHumanData);
            Logic.SupportFaceBumpEffect(ociChar.GetChaControl(), realHumanData);
        }        

        #endregion

        #region Public Methods
        #endregion

        #region Patches

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnSelectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(_node);   
                OCIChar ociChar = objectCtrlInfo as OCIChar;

                if (ociChar != null)
                {
                    _self._selectedOciChar = ociChar;

                    if (_self._ociCharMgmt.TryGetValue(ociChar.GetHashCode(), out var realHumanData))
                    {                    
                        if (realHumanData.coroutine != null)
                        {
                            ociChar.charInfo.StopCoroutine(realHumanData.coroutine);
                            ociChar.charInfo.StartCoroutine(_self.ExecuteAfterFrame(ociChar, realHumanData));
                        }                    
                    } 
                    else
                    {
                        RealHumanData realHumanData2 = new RealHumanData();
                        realHumanData2 = Logic.InitRealHumanData(ociChar.GetChaControl(), realHumanData2);

                        if (realHumanData2 != null)
                        {
                            realHumanData2.coroutine = ociChar.charInfo.StartCoroutine(_self.Routine(realHumanData2));                    
                            _self._ociCharMgmt.Add(ociChar.GetHashCode(), realHumanData2);
                            ociChar.charInfo.StartCoroutine(_self.ExecuteAfterFrame(ociChar, realHumanData2));
                        } else
                        {
                            Logger.LogMessage($"Body skin not has bumpmap2");
                        }                    
                    }    
                }

                return true;
            }
        }
    
        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeselectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnDeselectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                _self._selectedOciChar = null;
                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeleteNode), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnDeleteNode_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {        

                ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(_node);   
                OCIChar ociChar = objectCtrlInfo as OCIChar;

                _self._selectedOciChar = null;

                if (ociChar != null && _self._ociCharMgmt.TryGetValue(ociChar.GetHashCode(), out var realHumanData))
                {
                    if (realHumanData.coroutine != null)
                    {
                        ociChar.charInfo.StopCoroutine(realHumanData.coroutine);
                    }
                    realHumanData.c_m_eye.Clear();

                    _self._ociCharMgmt.Remove(ociChar.GetHashCode());
                }
                    
                return true;
            }
        }

        [HarmonyPatch(typeof(OCIChar), "ChangeChara", new[] { typeof(string) })]
        internal static class OCIChar_ChangeChara_Patches
        {
            public static void Postfix(OCIChar __instance, string _path)
            {
                ChaControl chaControl = __instance.GetChaControl();

                if (chaControl != null)
                {                    
                    if (_self._ociCharMgmt.TryGetValue(__instance.GetHashCode(), out var realHumanData))
                    {
                        _self._ociCharMgmt.Remove(__instance.GetHashCode());
                        if (realHumanData.coroutine != null)
                        {
                            __instance.charInfo.StopCoroutine(realHumanData.coroutine);
                        }
                        realHumanData.c_m_eye.Clear();

                        RealHumanData realHumanData2 = new RealHumanData();
                        realHumanData2 = Logic.InitRealHumanData(__instance.GetChaControl(), realHumanData2);

                        if (realHumanData2 != null)
                        {
                            realHumanData2.coroutine = __instance.charInfo.StartCoroutine(_self.Routine(realHumanData2));     
                            _self._ociCharMgmt.Add(__instance.GetHashCode(), realHumanData2);
                            __instance.charInfo.StartCoroutine(_self.ExecuteAfterFrame(__instance, realHumanData2));
                        } else
                        {
                            Logger.LogMessage($"Body skin not has bumpmap2");
                        }                    

                        _self._selectedOciChar = __instance;
                    }            
                }
            }
        }

        // 표정 부분 변경
        [HarmonyPatch(typeof(ChaControl), "ChangeEyesPtn", typeof(int), typeof(bool))]
        private static class ChaControl_ChangeEyesPtn_Patches
        {
            private static void Postfix(ChaControl __instance, int ptn, bool blend)
            {
                // UnityEngine.Debug.Log($">> ChangeEyesPtn {ptn}");

                OCIChar ociChar = __instance.GetOCIChar() as OCIChar;
                if (ociChar != null)
                {
                    if (_self._ociCharMgmt.TryGetValue(ociChar.GetHashCode(), out var realHumanData))
                    {
                        _self._eye_type = ptn;
                        Logic.SupportFaceBumpEffect(__instance, realHumanData);
                    }
                }
            }
        }     

        [HarmonyPatch(typeof(ChaControl), "ChangeMouthPtn", typeof(int), typeof(bool))]
        private static class ChaControl_ChangeMouthPtn_Patches
        {
            private static void Postfix(ChaControl __instance, int ptn, bool blend)
            {
                // UnityEngine.Debug.Log($">> ChangeMouthPtn {ptn}");
                OCIChar ociChar = __instance.GetOCIChar() as OCIChar;
                if (ociChar != null)
                {
                    if (_self._ociCharMgmt.TryGetValue(ociChar.GetHashCode(), out var realHumanData))
                    {
                        _self._mouth_type = ptn;
                        Logic.SupportFaceBumpEffect(__instance, realHumanData);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(PauseCtrl.FileInfo), "Apply", typeof(OCIChar))]
        private static class PauseCtrl_Apply_Patches
        {
            private static bool Prefix(PauseCtrl.FileInfo __instance, OCIChar _char)
            {
                if (_char != null)
                {
                    if (_self._ociCharMgmt.TryGetValue(_char.GetHashCode(), out var realHumanData))
                    {
                        Logic.SetHairDown(_char.GetChaControl(), realHumanData);
                    }
                }
                return true;
            }
        }        

        [HarmonyPatch(typeof(Studio.Studio), "InitScene", typeof(bool))]
        private static class Studio_InitScene_Patches
        {
            private static bool Prefix(object __instance, bool _close)
            {
                _self.SceneInit();
                return true;
            }
        }        
                        
        // In Game Mode
        
        // 위치 변경 시 마다
        [HarmonyPatch(typeof(ChaControl), "SetPosition", typeof(Vector3))]
        private static class ChaControl_SetPosition_Patches
        {
            private static void Postfix(ChaControl __instance, Vector3 pos)
            {
                if (!_self._isStudio) {
                    OCIChar ociChar = __instance.GetOCIChar() as OCIChar;
                    if (ociChar != null)
                    {
                        if (_self._ociCharMgmt.TryGetValue(ociChar.GetHashCode(), out var realHumanData))
                        {
                            Logic.SetHairDown(ociChar.GetChaControl(), realHumanData);
                        }
                    }
                }                
            }
        }
        
        // 위치 변경 시 마다
        [HarmonyPatch(typeof(Manager.HSceneManager), "SetFemaleState", typeof(ChaControl[]))]
        private static class HSceneManager_SetFemaleState_Patches
        {
            private static void Postfix(Manager.HSceneManager __instance, ChaControl[] female)
            {
                // player
                if (__instance.player != null)
                {
                    RealHumanData realHumanData = new RealHumanData();
                    realHumanData = Logic.InitRealHumanData(__instance.player, realHumanData);
                    Logic.SupportEyeFastBlinkEffect(__instance.player, realHumanData);                        
                    Logic.SupportExtraDynamicBones(__instance.player, realHumanData);
                }

                // heroine
                foreach (ChaControl chaCtrl in female)
                {
                    if (chaCtrl != null) {
                        RealHumanData realHumanData = new RealHumanData();
                        realHumanData = Logic.InitRealHumanData(chaCtrl, realHumanData);
                        Logic.SupportEyeFastBlinkEffect(chaCtrl, realHumanData);                        
                        Logic.SupportExtraDynamicBones(chaCtrl, realHumanData);
                    }
                }
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