using Studio;
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
        public const string Version = "0.9.4.0";
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
        internal static ConfigEntry<KeyboardShortcut> ConfigKeyRefreshWindShortcut { get; private set; }


        // Environment    
        internal static ConfigEntry<float> WindDirection { get; private set; }
        internal static ConfigEntry<float> WindInterval { get; private set; }
        internal static ConfigEntry<float> WindStrength { get; private set; }
        internal static ConfigEntry<float> WindUpward { get; private set; }


        internal static ConfigEntry<float> WindAccesoriesForce { get; private set; }
        internal static ConfigEntry<float> WindAccesoriesAmplitude { get; private set; }
        internal static ConfigEntry<float> AccesoriesDamping { get; private set; }
        internal static ConfigEntry<float> AccesoriesStiffness { get; private set; }


        internal static ConfigEntry<float> WindHairForce { get; private set; }
        internal static ConfigEntry<float> WindHairAmplitude { get; private set; }
        internal static ConfigEntry<float> HairDamping { get; private set; }
        internal static ConfigEntry<float> HairStiffness { get; private set; }


        internal static ConfigEntry<float> WindClotheForce { get; private set; }
        internal static ConfigEntry<float> WindClotheAmplitude { get; private set; }
        internal static ConfigEntry<float> ClothDamping { get; private set; }
        internal static ConfigEntry<float> ClothStiffness { get; private set; }


        private static string _assemblyLocation;
        private bool _loaded = false;
        private Status _status = Status.IDLE;

        private Cloth_Status _cloth_status = Cloth_Status.IDLE;

        private List<ObjectCtrlInfo> _selectedOCIs = new List<ObjectCtrlInfo>();

        private float _minY = float.MaxValue;
        private float _maxY = float.MinValue;

        // 위치에 따른 바람 강도
        private AnimationCurve _heightToForceCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.1f); // 위로 갈수록 약함

        private Dictionary<OCIChar, WindData> _ociCharMgmt = new Dictionary<OCIChar, WindData>();

        private Coroutine _oneSecondRoutine;


        internal enum Cloth_Status
        {
            PHYSICS,
            IDLE
        }

        internal enum Status
        {
            RUN,
            STOP,
            DESTROY,
            IDLE
        }

        #endregion

        #region Accessors
        internal static ConfigEntry<KeyboardShortcut> ConfigMainWindowShortcut { get; private set; }
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();
            // all 
            WindDirection = Config.Bind("All", "Direction", 0f, new ConfigDescription("wind direction", new AcceptableValueRange<float>(0.0f, 359.0f)));

            WindInterval = Config.Bind("All", "Interval", 2f, new ConfigDescription("wind spawn interval(sec)", new AcceptableValueRange<float>(0.0f, 10.0f)));

            WindStrength = Config.Bind("All", "Strength", 0.7f, new ConfigDescription("wind base strength", new AcceptableValueRange<float>(0.1f, 10.0f)));

            WindUpward = Config.Bind("All", "Upward", 0.1f, new ConfigDescription("wind blow upward", new AcceptableValueRange<float>(0.0f, 5.0f)));

            // accesories
            WindAccesoriesForce = Config.Bind("Misc", "Force", 0.5f, new ConfigDescription("wind force applied to accesories", new AcceptableValueRange<float>(0.1f, 10.0f)));

            WindAccesoriesAmplitude = Config.Bind("Misc", "Amplitude", 0.3f, new ConfigDescription("wind amplitude applied to accesories", new AcceptableValueRange<float>(0.0f, 10.0f)));

            AccesoriesDamping = Config.Bind("Misc", "Damping", 0.7f, new ConfigDescription("wind damping applied to accesories", new AcceptableValueRange<float>(0.0f, 1.0f)));

            AccesoriesStiffness = Config.Bind("Misc", "Stiffness", 1.0f, new ConfigDescription("wind stiffness applied to accesories", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // clothes
            WindClotheForce = Config.Bind("Cloth", "Force", 50f, new ConfigDescription("wind force applied to clothes", new AcceptableValueRange<float>(0.0f, 500.0f)));

            WindClotheAmplitude = Config.Bind("Cloth", "Amplitude", 0.5f, new ConfigDescription("wind amplitude applied to clothes", new AcceptableValueRange<float>(0.0f, 10.0f)));

            ClothDamping = Config.Bind("Cloth", "Damping", 0.5f, new ConfigDescription("wind damping applied to clothes", new AcceptableValueRange<float>(0.0f, 1.0f)));

            ClothStiffness = Config.Bind("Cloth", "Stiffness", 1.0f, new ConfigDescription("wind stiffness applied to clothes", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // hair
            WindHairForce = Config.Bind("Hair", "Force", 1f, new ConfigDescription("wind force applied to hairs", new AcceptableValueRange<float>(0.1f, 10.0f)));

            WindHairAmplitude = Config.Bind("Hair", "Amplitude", 0.5f, new ConfigDescription("wind amplitude applied to hairs", new AcceptableValueRange<float>(0.0f, 10.0f)));

            HairDamping = Config.Bind("Hair", "Damping", 0.5f, new ConfigDescription("wind damping applied to hairs", new AcceptableValueRange<float>(0.0f, 1.0f)));

            HairStiffness = Config.Bind("Hair", "Stiffness", 1.0f, new ConfigDescription("wind stiffness applied to hairs", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // 
            ConfigKeyEnableWind = Config.Bind("Options", "Toggle effect", false, "Wind enabled/disabled");

            ConfigKeyEnableWindShortcut = Config.Bind("ShortKey", "Toggle effect key", new KeyboardShortcut(KeyCode.W));

            ConfigKeyRefreshWindShortcut = Config.Bind("ShortKey", "Refresh effect", new KeyboardShortcut(KeyCode.R));


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

            if (ConfigKeyEnableWind.Value)
            {
                if (_selectedOCIs.Count > 0)
                    _status = Status.RUN;
                else
                    _status = Status.IDLE;
            }
            else
            {
                if (_status == Status.RUN)
                    _status = Status.STOP;
            }

            if (ConfigKeyRefreshWindShortcut.Value.IsDown())
            {
                if (_status == Status.IDLE)
                {
                    foreach (var kvp in _ociCharMgmt)
                    {
                        var key = kvp.Key;
                        WindData value = kvp.Value;
                        RefreshDynamicBones(key, value);
                    }
                }
                else
                {
                    foreach (var kvp in _ociCharMgmt)
                    {
                        var key = kvp.Key;
                        WindData value = kvp.Value;
                        ReallocateDynamicBones(key, value);
                    }
                }
            }
        }


        private void OnSceneLoad(string path)
        {
            _status = Status.STOP;
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


            _oneSecondRoutine = StartCoroutine(OneSecondRoutine());
        }

        private void SceneInit()
        {
            foreach (var kvp in _ociCharMgmt)
            {
                var key = kvp.Key;
                WindData value = kvp.Value;

                value.clothes.Clear();
                value.hairDynamicBones.Clear();

                key.charInfo.StopCoroutine(value.coroutine);
            }
            _ociCharMgmt.Clear();
            _selectedOCIs.Clear();
            _self._status = Status.DESTROY;
        }

        IEnumerator OneSecondRoutine()
        {
            while (true) // 무한 반복
            {
                foreach(ObjectCtrlInfo ctrlInfo in _selectedOCIs)
                {
                    OCIChar ociChar = ctrlInfo as OCIChar;
                    if (ociChar != null && _self._ociCharMgmt.TryGetValue(ociChar, out var windData))
                    {
                        windData.ClothDamping = ClothDamping.Value;
                        windData.ClothStiffness = ClothStiffness.Value;
                        windData.AccesoriesDamping = AccesoriesDamping.Value;
                        windData.AccesoriesStiffness = AccesoriesStiffness.Value;
                        windData.HairDamping = HairDamping.Value;
                        windData.HairStiffness = HairStiffness.Value;
                        windData.WindStrength = WindStrength.Value;
                        windData.WindAccesoriesAmplitude = WindAccesoriesAmplitude.Value;
                        windData.WindAccesoriesForce = WindAccesoriesForce.Value;
                        windData.WindClotheAmplitude = WindClotheAmplitude.Value;
                        windData.WindClotheForce = WindClotheForce.Value;
                        windData.WindHairAmplitude = WindHairAmplitude.Value;
                        windData.WindHairForce = WindHairForce.Value;
                        windData.WindInterval = WindInterval.Value;
                        windData.WindDirection = WindDirection.Value;
                        windData.WindUpward = WindUpward.Value;                        
                    }
                }            
                    
                yield return new WaitForSeconds(1.5f); // 1.5초 대기
            }
        }

        // private void ClearPhysicsEffect(WindData windData)
        // {
        //     _self._selectedOCI = null;
        //     windData.clothes.Clear();
        //     windData.hairDynamicBones.Clear();
        //     _self._status = Status.DESTROY;
        // }
        
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
                float timeWave = Mathf.Sin(time * windData.WindHairAmplitude + height) * windData.WindUpward; // 바람의 위 아래 폭 움직임 주기
                float timeFactor = Mathf.Lerp(0.1f, windData.WindHairForce, timeWave); // 바람의 강약 조절 

                Vector3 adjustedWind = windEffect * heightFactor * timeFactor;
                bone.m_Damping = windData.HairDamping;
                bone.m_Stiffness = windData.HairStiffness;
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
                float timeWave = Mathf.Sin(time * windData.WindAccesoriesAmplitude + height) * windData.WindUpward; // 바람의 위 아래 폭 움직임 주기
                float timeFactor = Mathf.Lerp(0.1f, windData.WindAccesoriesForce, timeWave); // 바람의 강약 조절

                Vector3 adjustedWind = windEffect * heightFactor * timeFactor;
                bone.m_Damping = windData.AccesoriesDamping;
                bone.m_Stiffness = windData.AccesoriesStiffness;
                bone.m_Force = adjustedWind;
                bone.m_Gravity = new Vector3(0, -0.015f, 0); // 아래 방향 중력
            }

            time = Time.time;
            foreach (var cloth in windData.clothes)
            {
                if (cloth == null)
                    continue;

                float offset = Random.Range(0f, Mathf.PI * 2f);
                float strength = Random.Range(0.5f, 1.5f);

                // 위/아래 출렁임 (Sine 함수 기반) -> 위는 강하게, 아래는 약하게 움직임..
                float sin = Mathf.Sin(time * windData.WindClotheAmplitude + offset);
                float verticalWave = sin * (sin >= 0f ? windData.WindUpward : 0.1f) * strength;

                // 좌/우 흔들림 (PerlinNoise 기반)
                float amplitude = Random.Range(0.1f, 0.5f); // 좌우 진폭 크기 (값이 작을수록 진동이 느리게...)
                float horizontalShake = (Mathf.PerlinNoise(time * amplitude, 0f) - 0.5f) * 2.0f * 1.5f; // X축 (좌우)        

                Vector3 externalWind = windEffect + new Vector3(0f, verticalWave, 0f);

                Vector3 randomWind = new Vector3(horizontalShake, verticalWave * 0.3f, 0f);
                cloth.useGravity = true;
                cloth.worldAccelerationScale = 0.5f; // 외부 가속도 반영 비율
                cloth.worldVelocityScale = 0.5f;
                cloth.randomAcceleration = randomWind * windData.WindClotheForce;
                cloth.damping = windData.ClothDamping;
                cloth.stiffnessFrequency = windData.ClothStiffness;
                cloth.externalAcceleration = externalWind.normalized * windData.WindClotheForce;
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
        }

        private IEnumerator WindRoutine(WindData windData)
        {
            while (!windData.shouldQuit)
            {
                if (_loaded == true)
                {
                    if (_status == Status.RUN)
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

                        Quaternion globalRotation = Quaternion.Euler(0f, windData.WindDirection, 0f);

                        // 방향에 랜덤성 부여 (약한 변화만 허용)
                        float angleY = Random.Range(-10, 10); // 좌우 유지
                        float angleX = Random.Range(-5, 5);   // 위/아래 유지 (음수면 아래 방향, 양수면 위 방향)
                        Quaternion localRotation = Quaternion.Euler(angleX, angleY, 0f);

                        Quaternion rotation = globalRotation * localRotation;

                        Vector3 direction = rotation * Vector3.back;

                        // 기본 바람 강도는 낮게 유지
                        Vector3 windEffect = direction.normalized * Random.Range(0.01f, windData.WindStrength);

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
                        if (windData.WindInterval > 0.1f)
                            yield return new WaitForSeconds(windData.WindInterval);

                    }
                    else if (_status == Status.STOP || _status == Status.DESTROY)
                    {
                        yield return StartCoroutine(FadeOutWind(windData, 2.5f));

                        if (_status == Status.DESTROY)
                        {
                            ClearWind(windData); // 최종 정리
                        }

                        _status = Status.IDLE;
                    }
                }

                if (_self._cloth_status == Cloth_Status.IDLE)
                    yield return new WaitForSeconds(1);
                else
                    yield return null;
            }
        }
        #endregion

        #region Patches

        private static IEnumerator ExecuteDynamicBoneAfterFrame(ObjectCtrlInfo objectCtrlInfo, WindData windData)
        {
            int frameCount = 30;
            for (int i = 0; i < frameCount; i++)
                yield return null;

            ReallocateDynamicBones(objectCtrlInfo, windData);
        }

        private static void RefreshDynamicBones(ObjectCtrlInfo objectCtrlInfo, WindData windData)
        {
            if (objectCtrlInfo != null)
            {

                // UnityEngine.Debug.Log($">> RefreshDynamicBones {_self._clothes.Count}");

                foreach (var cloth in windData.clothes)
                {
                    if (cloth == null)
                        continue;

                    cloth.damping = windData.ClothDamping;
                    cloth.stiffnessFrequency = windData.ClothStiffness;
                    cloth.externalAcceleration = Vector3.down * 5f;
                }
            }
        }

        private static void ReallocateDynamicBones(ObjectCtrlInfo objectCtrlInfo, WindData windData)
        {
            if (objectCtrlInfo != null)
            {   // set config 
                ClothDamping.Value = windData.ClothDamping;
                ClothStiffness.Value = windData.ClothStiffness;
                AccesoriesDamping.Value = windData.AccesoriesDamping;
                AccesoriesStiffness.Value = windData.AccesoriesStiffness;
                HairDamping.Value = windData.HairDamping;
                HairStiffness.Value = windData.HairStiffness;
                WindStrength.Value = windData.WindStrength;
                WindAccesoriesAmplitude.Value = windData.WindAccesoriesAmplitude;
                WindAccesoriesForce.Value = windData.WindAccesoriesForce;
                WindClotheAmplitude.Value = windData.WindClotheAmplitude;
                WindClotheForce.Value = windData.WindClotheForce;
                WindHairAmplitude.Value = windData.WindHairAmplitude;
                WindHairForce.Value = windData.WindHairForce;
                WindInterval.Value = windData.WindInterval;
                WindDirection.Value = windData.WindDirection;
                WindUpward.Value = windData.WindUpward;

                OCIChar ociChar = objectCtrlInfo as OCIChar;
                ChaControl baseCharControl = ociChar.charInfo;

                // Hair
                DynamicBone[] bones = baseCharControl.objBodyBone.transform.FindLoop("cf_J_Head").GetComponentsInChildren<DynamicBone>(true);
                windData.hairDynamicBones = bones.ToList();

                if (windData.hairDynamicBones.Count > 0)
                {
                    if (ConfigKeyEnableWind.Value)
                        _self._status = Status.RUN;
                }

                // accesories
                foreach (var accessory in baseCharControl.objAccessory)
                {
                    if (accessory == null)
                    {
                        continue;
                    }

                    if (accessory.GetComponentsInChildren<DynamicBone>().Length > 0)
                    {
                        windData.accesoriesDynamicBones.Add(accessory.GetComponentsInChildren<DynamicBone>()[0]);
                    }
                }

                // body
                Cloth[] clothes = baseCharControl.transform.GetComponentsInChildren<Cloth>(true);

                windData.clothes = clothes.ToList();

                _self._cloth_status = Cloth_Status.IDLE;
                if (windData.clothes.Count > 0)
                {
                    _self._cloth_status = Cloth_Status.PHYSICS;
                    if (ConfigKeyEnableWind.Value)
                        _self._status = Status.RUN;
                }
            }
        }
        
        private static WindData RegisterOciCharMgmt(OCIChar ociChar)
        {
            WindData windData = new WindData();
            windData.coroutine = ociChar.charInfo.StartCoroutine(_self.WindRoutine(windData));
            windData.ClothDamping = ClothDamping.Value;
            windData.ClothStiffness = ClothStiffness.Value;
            windData.AccesoriesDamping = AccesoriesDamping.Value;
            windData.AccesoriesStiffness = AccesoriesStiffness.Value;
            windData.HairDamping = HairDamping.Value;
            windData.HairStiffness = HairStiffness.Value;
            windData.WindStrength = WindStrength.Value;
            windData.WindAccesoriesAmplitude = WindAccesoriesAmplitude.Value;
            windData.WindAccesoriesForce = WindAccesoriesForce.Value;
            windData.WindClotheAmplitude = WindClotheAmplitude.Value;
            windData.WindClotheForce = WindClotheForce.Value;
            windData.WindHairAmplitude = WindHairAmplitude.Value;
            windData.WindHairForce = WindHairForce.Value;
            windData.WindInterval = WindInterval.Value;
            windData.WindDirection = WindDirection.Value;
            windData.WindUpward = WindUpward.Value;
            _self._ociCharMgmt.Add(ociChar, windData);

            return windData;       
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnSelectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(_node);

                // 기존 선택된 대상에서 동일 대상이 아니면 이전 대상 제거
                foreach(ObjectCtrlInfo ctrlInfo in _self._selectedOCIs)
                {
                    if (ctrlInfo != objectCtrlInfo)
                    {
                        OCIChar ociChar = ctrlInfo as OCIChar;
                        if (ociChar != null && _self._ociCharMgmt.TryGetValue(ociChar, out var windData))
                        {
                            if (windData.coroutine != null)
                            {
                                windData.shouldQuit = true;
                                // ociChar.charInfo.StopCoroutine(windData.coroutine);
                            }
                            _self._ociCharMgmt.Remove(ociChar);
                        }
                    }             
                }
            
                _self._selectedOCIs.Clear();
                _self._selectedOCIs.Add(objectCtrlInfo);       

                OCIChar ociChar2 = objectCtrlInfo as OCIChar;

                if (_self._ociCharMgmt.TryGetValue(ociChar2, out var windData1))
                {
                    ociChar2.GetChaControl().StartCoroutine(ExecuteDynamicBoneAfterFrame(ociChar2, windData1));
                }
                else
                {
                    WindData windData2 = RegisterOciCharMgmt(ociChar2);
                    ociChar2.GetChaControl().StartCoroutine(ExecuteDynamicBoneAfterFrame(ociChar2, windData2));
                }
                
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
                    OCIChar ociChar = objectCtrlInfo as OCIChar;
                    if (_self._ociCharMgmt.TryGetValue(ociChar, out var windData1))
                    {
                        ociChar.GetChaControl().StartCoroutine(ExecuteDynamicBoneAfterFrame(ociChar, windData1));
                    }
                    else
                    {
                        WindData windData2 = RegisterOciCharMgmt(ociChar);
                        ociChar.GetChaControl().StartCoroutine(ExecuteDynamicBoneAfterFrame(ociChar, windData2));
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

                foreach (ObjectCtrlInfo ctrlInfo in _self._selectedOCIs)
                {
                    OCIChar ociChar = ctrlInfo as OCIChar;
                    if (ociChar != null && _self._ociCharMgmt.TryGetValue(ociChar, out var windData))
                    {
                        if (windData.coroutine != null)
                        {
                            windData.shouldQuit = true;
                            // ociChar.charInfo.StopCoroutine(windData.coroutine);
                        }
                        _self._ociCharMgmt.Remove(ociChar);
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

                    if (ociChar != null && _self._ociCharMgmt.TryGetValue(ociChar, out var windData))
                    {
                        if (windData.coroutine != null)
                        {
                            ociChar.charInfo.StopCoroutine(windData.coroutine);
                        }
                    }

                    _self._ociCharMgmt.Remove(ociChar);
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
                    if (_self._ociCharMgmt.TryGetValue(chaControl.GetOCIChar(), out var windData))
                        chaControl.StartCoroutine(ExecuteDynamicBoneAfterFrame(__instance as OCIChar, windData));
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
                    if (_self._ociCharMgmt.TryGetValue(ociChar, out var windData))
                        __instance.StartCoroutine(ExecuteDynamicBoneAfterFrame(ociChar, windData));
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
                    if (_self._ociCharMgmt.TryGetValue(ociChar, out var windData))
                        __instance.StartCoroutine(ExecuteDynamicBoneAfterFrame(ociChar, windData));
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
                    if (_self._ociCharMgmt.TryGetValue(ociChar, out var windData))
                        __instance.StartCoroutine(ExecuteDynamicBoneAfterFrame(ociChar, windData));
                }
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

        #endregion
    }   
    
    class WindData
    {
        public Coroutine coroutine;

        public List<DynamicBone> hairDynamicBones = new List<DynamicBone>();

        public List<DynamicBone> accesoriesDynamicBones = new List<DynamicBone>();

        public List<Cloth> clothes = new List<Cloth>();

        public float WindDirection;
        public float WindInterval;
        public float WindStrength;
        public float WindUpward;

        public float WindAccesoriesForce;
        public float WindAccesoriesAmplitude;
        public float AccesoriesDamping;
        public float AccesoriesStiffness;


        public float WindHairForce;
        public float WindHairAmplitude;
        public float HairDamping;
        public float HairStiffness;


        public float WindClotheForce;
        public float WindClotheAmplitude;
        public float ClothDamping;
        public float ClothStiffness;

        public bool shouldQuit = false;

        public WindData()
        {
        }        
    }

}