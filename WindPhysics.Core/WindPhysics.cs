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

// 추가 작업 예정
// - direction 자동 360도 회전

namespace WindPhysics
{
#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class WindPhysics : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "WindPhysics";
        public const string Version = "0.9.4.1";
        public const string GUID = "com.alton.illusionplugins.wind";
        internal const string _ownerId = "alton";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "wind_physics";
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
        internal static WindPhysics _self;

        internal static Dictionary<string, string> boneDict = new Dictionary<string, string>();

        internal static ConfigEntry<bool> ConfigKeyEnableWind { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ConfigKeyEnableWindShortcut { get; private set; }


        // Environment    
        internal static ConfigEntry<float> WindForward { get; private set; }
        internal static ConfigEntry<float> WindUpward { get; private set; }
        internal static ConfigEntry<float> WindInterval { get; private set; }
        internal static ConfigEntry<float> WindForce { get; private set; }


        internal static ConfigEntry<float> WindAccesoriesAmplitude { get; private set; }
        internal static ConfigEntry<float> AccesoriesDamping { get; private set; }
        internal static ConfigEntry<float> AccesoriesStiffness { get; private set; }


        internal static ConfigEntry<float> WindHairAmplitude { get; private set; }
        internal static ConfigEntry<float> HairDamping { get; private set; }
        internal static ConfigEntry<float> HairStiffness { get; private set; }


        internal static ConfigEntry<float> WindClotheAmplitude { get; private set; }
        internal static ConfigEntry<float> ClothDamping { get; private set; }
        internal static ConfigEntry<float> ClothStiffness { get; private set; }


        private static string _assemblyLocation;
        private bool _loaded = false;

        private List<ObjectCtrlInfo> _selectedOCIs = new List<ObjectCtrlInfo>();

        private float _minY = float.MaxValue;
        private float _maxY = float.MinValue;

        private bool _previousConfigKeyEnableWind;

        // 위치에 따른 바람 강도
        private AnimationCurve _heightToForceCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.1f); // 위로 갈수록 약함

        private Dictionary<int, WindData> _ociObjectMgmt = new Dictionary<int, WindData>();

        private Coroutine _IterativeRoutine;

        #endregion

        #region Accessors
        internal static ConfigEntry<KeyboardShortcut> ConfigMainWindowShortcut { get; private set; }
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();
            // all 
            WindForward = Config.Bind("All", "direction", 0f, new ConfigDescription("wind 360", new AcceptableValueRange<float>(0.0f, 359.0f)));

            WindUpward = Config.Bind("All", "updown", 0.0f, new ConfigDescription("wind up or down", new AcceptableValueRange<float>(0.0f, 5.0f)));

            WindInterval = Config.Bind("All", "Interval", 2f, new ConfigDescription("wind spawn interval(sec)", new AcceptableValueRange<float>(0.0f, 10.0f)));
            
            WindForce = Config.Bind("All", "Force", 0.5f, new ConfigDescription("wind force", new AcceptableValueRange<float>(0.1f, 10.0f)));

            // accesories
            WindAccesoriesAmplitude = Config.Bind("Misc", "Amplitude", 0.3f, new ConfigDescription("wind amplitude applied to accesories", new AcceptableValueRange<float>(0.0f, 10.0f)));

            AccesoriesDamping = Config.Bind("Misc", "Damping", 0.7f, new ConfigDescription("wind damping applied to accesories", new AcceptableValueRange<float>(0.0f, 1.0f)));

            AccesoriesStiffness = Config.Bind("Misc", "Stiffness", 1.0f, new ConfigDescription("wind stiffness applied to accesories", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // clothes
            WindClotheAmplitude = Config.Bind("Cloth", "Amplitude", 0.5f, new ConfigDescription("wind amplitude applied to clothes", new AcceptableValueRange<float>(0.0f, 10.0f)));

            ClothDamping = Config.Bind("Cloth", "Damping", 0.5f, new ConfigDescription("wind damping applied to clothes", new AcceptableValueRange<float>(0.0f, 1.0f)));

            ClothStiffness = Config.Bind("Cloth", "Stiffness", 1.0f, new ConfigDescription("wind stiffness applied to clothes", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // hair
            WindHairAmplitude = Config.Bind("Hair", "Amplitude", 0.5f, new ConfigDescription("wind amplitude applied to hairs", new AcceptableValueRange<float>(0.0f, 10.0f)));

            HairDamping = Config.Bind("Hair", "Damping", 0.5f, new ConfigDescription("wind damping applied to hairs", new AcceptableValueRange<float>(0.0f, 1.0f)));

            HairStiffness = Config.Bind("Hair", "Stiffness", 1.0f, new ConfigDescription("wind stiffness applied to hairs", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // 
            ConfigKeyEnableWind = Config.Bind("Options", "Toggle effect", false, "Wind enabled/disabled");

            ConfigKeyEnableWindShortcut = Config.Bind("ShortKey", "Toggle effect key", new KeyboardShortcut(KeyCode.W));

            _previousConfigKeyEnableWind = ConfigKeyEnableWind.Value;


            _self = this;
            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            ExtensibleSaveFormat.ExtendedSave.SceneBeingLoaded += OnSceneLoad;

            var harmony = HarmonyExtensions.CreateInstance(GUID);
            harmony.PatchAll(Assembly.GetExecutingAssembly());                     
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

            if (ConfigKeyEnableWindShortcut.Value.IsDown())
            {
                ConfigKeyEnableWind.Value = !ConfigKeyEnableWind.Value;
            }
        }

        private void OnSceneLoad(string path)
        {           
            _selectedOCIs.Clear();
        }

        #endregion

        #region Public Methods

        #endregion

        #region Private Methods
        private void Init()
        {
            UIUtility.Init();
            _loaded = true;


            _IterativeRoutine = StartCoroutine(IterativeRoutine());
        }

        private void SceneInit()
        {
            foreach (var kvp in _ociObjectMgmt)
            {
                var key = kvp.Key;
                WindData value = kvp.Value;

                value.clothes.Clear();
                value.hairDynamicBones.Clear();
                OCIChar ociChar = value.objectCtrlInfo as OCIChar;

                if (ociChar != null)
                {
                    if (value.coroutine != null)
                    {
                        ociChar.charInfo.StopCoroutine(value.coroutine);
                        value.coroutine = null;
                    }
                }

                OCIItem ociItem = value.objectCtrlInfo as OCIItem;
                if (ociItem != null)
                {
                    if (value.coroutine != null)
                    {
                        ociItem.guideObject.StopCoroutine(value.coroutine);
                        value.coroutine = null;
                    }
                }
            }
            _ociObjectMgmt.Clear();
            _selectedOCIs.Clear();
        }

        IEnumerator IterativeRoutine()
        {
            while (true) // 무한 반복
            {
                if(_previousConfigKeyEnableWind != ConfigKeyEnableWind.Value)
                {
                    foreach (ObjectCtrlInfo ctrlInfo in _selectedOCIs)
                    {
                        OCIChar ociChar = ctrlInfo as OCIChar;
                        if (ociChar != null && _self._ociObjectMgmt.TryGetValue(ociChar.GetHashCode(), out var windData))
                        {                          
                            if (ConfigKeyEnableWind.Value)
                            {
                                OCIChar ociChar1 = windData.objectCtrlInfo as OCIChar;

                                if (windData.wind_status == Status.DESTROY)
                                {
                                    if (windData.coroutine != null)
                                    {                                       
                                        ociChar1.charInfo.StopCoroutine(windData.coroutine);
                                        windData.coroutine = null;
                                    }
                                    
                                    windData.wind_status = Status.RUN;
                                    windData.coroutine = ociChar1.charInfo.StartCoroutine(WindRoutine(windData));
                                }
                            } else
                            {
                                windData.wind_status = Status.DESTROY;
                            }                            
                        }
                    }                        

                    _previousConfigKeyEnableWind = ConfigKeyEnableWind.Value;          
                }

                yield return new WaitForSeconds(1.0f); // 1.0초 대기
            }
        }

        void ApplyWind(Vector3 windEffect, float factor, WindData windData) // 기본 바람 방향
        {
            windEffect = windEffect * factor;

            float time = Time.time;

            foreach (var bone in windData.hairDynamicBones)
            {
                if (bone == null)
                    continue;

                float height = bone.m_Root.position.y;

                float normalizedHeight = Mathf.InverseLerp(_minY, _maxY, height);
                float heightFactor = _heightToForceCurve.Evaluate(normalizedHeight);
                float timeWave = Mathf.Sin(time * WindHairAmplitude.Value + height) * WindUpward.Value; // 바람의 위 아래 폭 움직임 주기
                float timeFactor = Mathf.Lerp(0.1f, WindForce.Value, timeWave); // 바람의 강약 조절 

                Vector3 adjustedWind = windEffect * heightFactor * timeFactor;
                bone.m_Damping = HairDamping.Value;
                bone.m_Stiffness = HairStiffness.Value;
                bone.m_Force = adjustedWind;
                bone.m_Gravity = new Vector3(0, -0.005f, 0); // 아래 방향 중력
            }

            foreach (var bone in windData.accesoriesDynamicBones)
            {
                if (bone == null)
                    continue;

                float height = bone.m_Root.position.y;

                float normalizedHeight = Mathf.InverseLerp(_minY, _maxY, height);
                float heightFactor = _heightToForceCurve.Evaluate(normalizedHeight);
                float timeWave = Mathf.Sin(time * WindAccesoriesAmplitude.Value + height) * WindUpward.Value; // 바람의 위 아래 폭 움직임 주기
                float timeFactor = Mathf.Lerp(0.1f, WindForce.Value, timeWave); // 바람의 강약 조절

                Vector3 adjustedWind = windEffect * heightFactor * timeFactor;
                bone.m_Damping = AccesoriesDamping.Value;
                bone.m_Stiffness = AccesoriesStiffness.Value;
                bone.m_Force = adjustedWind;
                bone.m_Gravity = new Vector3(0, -0.015f, 0); // 아래 방향 중력
            }

            time = Time.time;
            foreach (var cloth in windData.clothes)
            {
                if (cloth == null)
                    continue;

                float offset = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float strength = UnityEngine.Random.Range(0.5f, 1.5f);

                // 위/아래 출렁임 (Sine 함수 기반) -> 위는 강하게, 아래는 약하게 움직임..
                float sin = Mathf.Sin(time * WindClotheAmplitude.Value + offset);
                float verticalWave = sin * (sin >= 0f ? WindUpward.Value : 0.1f) * strength;

                // 좌/우 흔들림 (PerlinNoise 기반)
                float amplitude = UnityEngine.Random.Range(0.1f, 0.5f); // 좌우 진폭 크기 (값이 작을수록 진동이 느리게...)
                float horizontalShake = (Mathf.PerlinNoise(time * amplitude, 0f) - 0.5f) * 2.0f * 1.5f; // X축 (좌우)        

                Vector3 externalWind = windEffect + new Vector3(0f, verticalWave, 0f);

                Vector3 randomWind = new Vector3(horizontalShake, verticalWave * 0.3f, 0f);
                cloth.useGravity = true;
                cloth.worldAccelerationScale = 0.5f; // 외부 가속도 반영 비율
                cloth.worldVelocityScale = 0.5f;
                cloth.randomAcceleration = randomWind * WindForce.Value * 35;
                cloth.damping = ClothDamping.Value;
                cloth.stiffnessFrequency = ClothStiffness.Value;
                cloth.externalAcceleration = externalWind.normalized * WindForce.Value * 35;
            }
        }

        private IEnumerator FadeOutWind(WindData windData, float fadeTime)
        {
            const float step = 0.3f; // 0.3초 단위
            int steps = Mathf.CeilToInt(fadeTime / step);

            // 초기 값 저장
            var initialRandomAcceleration = new Dictionary<Cloth, Vector3>();
            var initialExternalAcceleration = new Dictionary<Cloth, Vector3>();

            foreach (var cloth in windData.clothes)
            {
                if (cloth == null) continue;

                initialRandomAcceleration[cloth] = cloth.randomAcceleration;
                initialExternalAcceleration[cloth] = cloth.externalAcceleration;
            }

            // Fade loop (Ease Out)
            for (int i = 0; i < steps; i++)
            {
                float normalized = (i + 1) / (float)steps; // 0~1
                float factor = 1f - Mathf.Pow(1f - normalized, 3); // Ease Out Cubic

                foreach (var cloth in windData.clothes)
                {
                    if (cloth == null) continue;

                    if (initialRandomAcceleration.TryGetValue(cloth, out var rnd))
                        cloth.randomAcceleration = rnd * (1f - factor);

                    if (initialExternalAcceleration.TryGetValue(cloth, out var ext))
                        cloth.externalAcceleration = ext * (1f - factor);
                }

                yield return new WaitForSeconds(step);
            }
        }

        private void ClearWind(WindData windData)
        {
            windData.clothes.Clear();
            windData.hairDynamicBones.Clear();
            windData.accesoriesDynamicBones.Clear();

            if (windData.coroutine != null)
            {
                OCIChar ocichar = windData.objectCtrlInfo as OCIChar;
                ocichar.charInfo.StopCoroutine(windData.coroutine);
                windData.coroutine = null;
            }
        }

        private IEnumerator WindRoutine(WindData windData)
        {
            while (true)
            {
                if (_loaded == true)
                {
                    if (windData.wind_status == Status.RUN)
                    {
                        // y 위치 기반 바람세기 처리를 위한 위치 정보 획득
                        foreach (var bone in windData.hairDynamicBones)
                        {
                            if (bone == null)
                                continue;

                            float y = bone.m_Root.position.y;
                            _minY = Mathf.Min(_minY, y);
                            _maxY = Mathf.Max(_maxY, y);
                        }

                        Quaternion globalRotation = Quaternion.Euler(0f, WindForward.Value, 0f);

                        // 방향에 랜덤성 부여 (약한 변화만 허용)
                        float angleY = UnityEngine.Random.Range(-10, 10); // 좌우 유지
                        float angleX = UnityEngine.Random.Range(-5, 5);   // 위/아래 유지 (음수면 아래 방향, 양수면 위 방향)
                        Quaternion localRotation = Quaternion.Euler(angleX, angleY, 0f);

                        Quaternion rotation = globalRotation * localRotation;

                        Vector3 direction = rotation * Vector3.back;

                        // 기본 바람 강도는 낮게 유지
                        Vector3 windEffect = direction.normalized * UnityEngine.Random.Range(0.01f, 0.5f);

                        // 적용
                        ApplyWind(windEffect, 1.0f, windData);
                        yield return new WaitForSeconds(0.5f);

                        // 자연스럽게 사라짐
                        // float fadeTime = Random.Range(0.3f, 1.5f);
                        float fadeTime = 1.5f;
                        float t = 0f;
                        while (t < fadeTime)
                        {
                            t += Time.deltaTime;
                            float factor = Mathf.SmoothStep(1f, 0f, t / fadeTime); // 부드러운 감소                                
                            ApplyWind(windEffect, factor, windData);
                            yield return null;
                        }

                        // 다음 바람 전 잠깐 멈춤
                        if (WindInterval.Value > 0.1f)
                            yield return new WaitForSeconds(WindInterval.Value);

                    }
                    else if (windData.wind_status == Status.STOP || windData.wind_status == Status.DESTROY)
                    {
                        yield return StartCoroutine(FadeOutWind(windData, 0.3f));

                        if (windData.wind_status == Status.DESTROY)
                        {
                            yield break;
                        }

                        windData.wind_status = Status.IDLE;
                    }
                }

                if (windData.cloth_status == Cloth_Status.EMPTY)
                    yield return new WaitForSeconds(1);
                else
                    yield return null;
            }
        }
        #endregion

        #region Patches

        private static WindData CreateWindData(ObjectCtrlInfo ociChar)
        {
            WindData windData = new WindData();
            windData.objectCtrlInfo = ociChar;
            _self._ociObjectMgmt.Add(ociChar.GetHashCode(), windData);

            return windData;
        }

        private static IEnumerator ExecuteDynamicBoneAfterFrame(WindData windData)
        {
            int frameCount = 20;
            for (int i = 0; i < frameCount; i++)
                yield return null;

            ReallocateDynamicBones(windData);
        }

        private static void ReallocateDynamicBones(WindData windData)
        {

            if (windData.objectCtrlInfo != null)
            {
                OCIChar ociChar = windData.objectCtrlInfo as OCIChar;
                OCIItem ociItem = windData.objectCtrlInfo as OCIItem;
                if (ociChar != null) {
                    ChaControl baseCharControl = ociChar.charInfo;

                    // 기존 자원 제거
                    _self.ClearWind(windData);

                    // 신규 자원 할당
                    // Hair
                    DynamicBone[] bones = baseCharControl.objBodyBone.transform.FindLoop("cf_J_Head").GetComponentsInChildren<DynamicBone>(true);
                    windData.hairDynamicBones = bones.ToList();

                    // Accesories
                    foreach (var accessory in baseCharControl.objAccessory)
                    {
                        if (accessory != null && accessory.GetComponentsInChildren<DynamicBone>().Length > 0)
                        {
                            windData.accesoriesDynamicBones.Add(accessory.GetComponentsInChildren<DynamicBone>()[0]);
                        }
                    }

                    // Cloth
                    Cloth[] clothes = baseCharControl.transform.GetComponentsInChildren<Cloth>(true);

                    windData.clothes = clothes.ToList();
                    windData.cloth_status = windData.clothes.Count > 0 ? Cloth_Status.PHYSICS : Cloth_Status.EMPTY;

                    // Coroutine
                    windData.wind_status = ConfigKeyEnableWind.Value ? Status.RUN : Status.DESTROY;
                    windData.coroutine = ConfigKeyEnableWind.Value ? ociChar.charInfo.StartCoroutine(_self.WindRoutine(windData)) : null;  
                }

                if (ociItem != null) {
                    DynamicBone[] bones = ociItem.guideObject.transformTarget.gameObject.GetComponentsInChildren<DynamicBone>(true);
                    Cloth[] clothes = ociItem.guideObject.transformTarget.gameObject.GetComponentsInChildren<Cloth>(true);

                    windData.hairDynamicBones = bones.ToList();
                    windData.clothes = clothes.ToList();

                    windData.wind_status = ConfigKeyEnableWind.Value ? Status.RUN : Status.DESTROY;
                    windData.coroutine = ConfigKeyEnableWind.Value ? ociItem.guideObject.StartCoroutine(_self.WindRoutine(windData)) : null;                      
                }               
            }
        }

        private static void TryAllocateObject(ObjectCtrlInfo objectItem, bool isSingle = true) {

            OCIChar selectedOciChar = objectItem as OCIChar; 
            OCIItem selectedOciItem = objectItem as OCIItem;             

            if (isSingle) {
                // 기존 선택된 대상에서 동일 대상이 아니면 이전 대상은 동작 멈춤
                if (selectedOciChar != null) {
                    foreach (ObjectCtrlInfo ctrlInfo in _self._selectedOCIs)
                    {
                        OCIChar ociChar = ctrlInfo as OCIChar;
                        OCIItem ociItem = ctrlInfo as OCIItem;
                        if (ociChar != null) {
                            if (ociChar != selectedOciChar)
                            {
                                if (_self._ociObjectMgmt.TryGetValue(ociChar.GetHashCode(), out var windData))
                                {
                                    if (windData.coroutine != null)
                                    {
                                        windData.wind_status = Status.STOP;
                                    }
                                }
                            }                   
                        }                  
                    }
                }
                else if(selectedOciItem != null) {
                    foreach (ObjectCtrlInfo ctrlInfo in _self._selectedOCIs)
                    {
                        OCIItem ociItem = ctrlInfo as OCIItem;
                        if (ociItem != null) {
                            if (ociItem != selectedOciItem)
                            {
                                if (_self._ociObjectMgmt.TryGetValue(ociItem.GetHashCode(), out var windData))
                                {
                                    if (windData.coroutine != null)
                                    {
                                        windData.wind_status = Status.STOP;
                                    }
                                }
                            }                   
                        }                    
                    }                
                }
            }

            if (selectedOciChar != null || selectedOciItem != null)
                _self._selectedOCIs.Clear();

            if (selectedOciChar != null)
            {
                {
                    _self._selectedOCIs.Add(selectedOciChar);

                    if (_self._ociObjectMgmt.TryGetValue(selectedOciChar.GetHashCode(), out var windData1))
                    {
                        if (windData1.wind_status == Status.RUN)
                        {
                        }
                        else if (windData1.wind_status == Status.STOP || windData1.wind_status == Status.IDLE)
                        {
                            windData1.wind_status = Status.RUN;                            
                        } else
                        {
                            selectedOciChar.GetChaControl().StartCoroutine(ExecuteDynamicBoneAfterFrame(windData1));
                        }
                    }
                    else
                    {
                        WindData windData2 = CreateWindData(selectedOciChar);
                        selectedOciChar.GetChaControl().StartCoroutine(ExecuteDynamicBoneAfterFrame(windData2));
                    }                          
                }                  
            }

            if (selectedOciItem != null)
            {
                {
                    _self._selectedOCIs.Add(selectedOciItem);

                    if (_self._ociObjectMgmt.TryGetValue(selectedOciItem.GetHashCode(), out var windData1))
                    {
                        if (windData1.wind_status == Status.RUN)
                        {
                        }
                        else if (windData1.wind_status == Status.STOP || windData1.wind_status == Status.IDLE)
                        {
                            windData1.wind_status = Status.RUN;                            
                        } else
                        {
                            selectedOciItem.guideObject.StartCoroutine(ExecuteDynamicBoneAfterFrame(windData1));
                        }
                    }
                    else
                    {
                        WindData windData2 = CreateWindData(selectedOciItem);
                        selectedOciItem.guideObject.StartCoroutine(ExecuteDynamicBoneAfterFrame(windData2));
                    }                          
                }                  
            }                
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnSelectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(_node);
                                
                TryAllocateObject(objectCtrlInfo);
             
                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectMultiple))]
        private static class WorkspaceCtrl_OnSelectMultiple_Patches
        {
            private static bool Prefix(object __instance)
            {
                foreach (TreeNodeObject node in Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes)
                {
                   ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(node);
                   TryAllocateObject(objectCtrlInfo, false);                   
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeselectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnDeselectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {

                foreach (ObjectCtrlInfo ctrlInfo in _self._selectedOCIs)
                {
                    OCIChar ociChar = ctrlInfo as OCIChar;
                    if (ociChar != null && _self._ociObjectMgmt.TryGetValue(ociChar.GetHashCode(), out var windData1))
                    {
                        if (windData1.coroutine != null)
                        {
                            windData1.wind_status = Status.STOP;
                        }
                    }

                    OCIItem ociItem = ctrlInfo as OCIItem;
                   if (ociItem != null && _self._ociObjectMgmt.TryGetValue(ociItem.GetHashCode(), out var windData2))
                    {
                        if (windData2.coroutine != null)
                        {
                            windData2.wind_status = Status.STOP;
                        }
                    }
                }

                _self._selectedOCIs.Clear();

                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeleteNode), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnDeleteNode_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {

                foreach (ObjectCtrlInfo ctrlInfo in _self._selectedOCIs)
                {
                    OCIChar ociChar = ctrlInfo as OCIChar;

                    if (ociChar != null) {
                        if (_self._ociObjectMgmt.TryGetValue(ociChar.GetHashCode(), out var windData))
                        {
                            if (windData.coroutine != null)
                            {
                                windData.wind_status = Status.DESTROY;                            
                            }
                        }
                        _self._ociObjectMgmt.Remove(ociChar.GetHashCode());
                    }

                    OCIItem ociItem = ctrlInfo as OCIItem;
                    if (ociItem != null) {
                        if (_self._ociObjectMgmt.TryGetValue(ociItem.GetHashCode(), out var windData))
                        {
                            if (windData.coroutine != null)
                            {
                                windData.wind_status = Status.DESTROY;                     
                            }
                        }
                        _self._ociObjectMgmt.Remove(ociItem.GetHashCode());
                    }                    
                }

                _self._selectedOCIs.Clear();

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
                    if (_self._ociObjectMgmt.TryGetValue(chaControl.GetHashCode(), out var windData))
                        chaControl.StartCoroutine(ExecuteDynamicBoneAfterFrame(windData));
                }
            }
        }

        // 개별 옷 변경
        [HarmonyPatch(typeof(ChaControl), "ChangeClothes", typeof(int), typeof(int), typeof(bool))]
        private static class ChaControl_ChangeClothes_Patches
        {
            private static void Postfix(ChaControl __instance, int kind, int id, bool forceChange)
            {
                OCIChar ociChar = __instance.GetOCIChar() as OCIChar;

                if (ociChar != null)
                {
                    if (_self._ociObjectMgmt.TryGetValue(ociChar.GetHashCode(), out var windData))
                        __instance.StartCoroutine(ExecuteDynamicBoneAfterFrame(windData));
                }
            }
        }

        // 코디네이션 변경
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.SetAccessoryStateAll), typeof(bool))]
        internal static class ChaControl_SetAccessoryStateAll_Patches
        {
            public static void Postfix(ChaControl __instance, bool show)
            {
                OCIChar ociChar = __instance.GetOCIChar() as OCIChar;

                if (ociChar != null)
                {
                    if (_self._ociObjectMgmt.TryGetValue(ociChar.GetHashCode(), out var windData))
                        __instance.StartCoroutine(ExecuteDynamicBoneAfterFrame(windData));
                }
            }
        }

        // 악세러리 변경
        [HarmonyPatch(typeof(ChaControl), "ChangeAccessory", typeof(int), typeof(int), typeof(int), typeof(string), typeof(bool))]
        private static class ChaControl_ChangeAccessory_Patches
        {
            private static void Postfix(ChaControl __instance, int slotNo, int type, int id, string parentKey, bool forceChange)
            {
                OCIChar ociChar = __instance.GetOCIChar() as OCIChar;

                if (ociChar != null)
                {
                    if (_self._ociObjectMgmt.TryGetValue(ociChar.GetHashCode(), out var windData))
                        __instance.StartCoroutine(ExecuteDynamicBoneAfterFrame(windData));
                }
            }
        }

        //[HarmonyPatch(typeof(OCIItem), "OnVisible", typeof(bool))]
        //private static class OCIItem_OnVisible_Patches
        //{
        //    private static void Postfix(OCIItem __instance, bool _visible)
        //    {                
        //        OCIItem item = __instance as OCIItem;
        //        UnityEngine.Debug.Log($">> OCIItem OnVisible {item.guideObject.name}");

        //    }
        //}

        //[HarmonyPatch(typeof(OCIItem), "OnDelete")]
        //private static class OCIItem_OnDelete_Patches
        //{
        //    private static void Postfix(OCIItem __instance)
        //    {                
        //        // OCIItem item = _child as OCIItem;
        //        UnityEngine.Debug.Log($">> OCIItem OnDelete");
        //    }
        //}   

        [HarmonyPatch(typeof(Studio.Studio), "InitScene", typeof(bool))]
        private static class Studio_InitScene_Patches
        {
            private static bool Prefix(object __instance, bool _close)
            {
                _self.SceneInit();
                return true;
            }
        }

        #endregion
    }   
    
    enum Cloth_Status
    {
        PHYSICS,
        EMPTY
    }

    enum Status
    {
        RUN,
        STOP,
        DESTROY,
        IDLE
    }


    class WindData
    {
        public ObjectCtrlInfo objectCtrlInfo;

        public Coroutine coroutine;
        public List<Cloth> clothes = new List<Cloth>();        

        public List<DynamicBone> hairDynamicBones = new List<DynamicBone>();

        public List<DynamicBone> accesoriesDynamicBones = new List<DynamicBone>();

        public SkinnedMeshRenderer clothTopRender;

        public SkinnedMeshRenderer clothBottomRender;

        public SkinnedMeshRenderer hairRender;

        public SkinnedMeshRenderer headRender;

        public SkinnedMeshRenderer bodyRender;

        public Status wind_status = Status.IDLE;

        public Cloth_Status cloth_status = Cloth_Status.EMPTY;

        public WindData()
        {
        }
    }

}