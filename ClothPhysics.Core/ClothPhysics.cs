using Studio;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
//using System.Reflection.Emit;
//using System.Text;
//using System.Text.RegularExpressions;
using BepInEx.Logging;
using ToolBox;
using ToolBox.Extensions;
using UILib;
//using UILib.ContextMenu;
//using UILib.EventHandlers;
using UnityEngine;
//using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
//using UnityEngine.UI;

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
using static DynamicBone_Ver01;
#endif

namespace ClothPhysics
{
#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class ClothPhysics : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "ClothPhysics";
        public const string Version = "0.9.0";
        public const string GUID = "com.alton.illusionplugins.clothphysics";
        internal const string _ownerId = "alton";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "cloth_physics";
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
        internal static ClothPhysics _self;

        internal static Dictionary<string, string> boneDict = new Dictionary<string, string>();

        // internal static ConfigEntry<bool> ConfigKeyEnableTight { get; private set; }

        // internal static ConfigEntry<float> ClothTightStrength { get; private set; }

        internal static ConfigEntry<float> ClothMaxDistanceTop { get; private set; }

        internal static ConfigEntry<float> ClothMaxDistanceMiddle { get; private set; }

        internal static ConfigEntry<float> ClothMaxDistanceBottom { get; private set; }

        internal static ConfigEntry<float> ClothAccBottom { get; private set; }

        internal static ConfigEntry<float> ClothDamping { get; private set; }

        internal static ConfigEntry<float> ClothStiffness { get; private set; }


        internal static ConfigEntry<KeyboardShortcut> ConfigKeyEnableUndressShortcut { get; private set; }

        // internal static ConfigEntry<KeyboardShortcut> ConfigKeyPrintShortcut { get; private set; }

        // Test
        internal static ConfigEntry<float> ClothUndressDuration { get; private set; }

        internal static ConfigEntry<float> ClothMaxDistance { get; private set; }


        private static string _assemblyLocation;
        private bool _loaded = false;
        private Status _status = Status.IDLE;
        private ObjectCtrlInfo _selectedOCI;

        private Dictionary<Cloth, float[]> _originalMaxDistances = new Dictionary<Cloth, float[]>();

        private Transform _pelvisBone;

        private Coroutine _clothCoroutine;

        private List<Cloth> _clothes = new List<Cloth>();

        private SkinnedMeshRenderer _selectedOciSmr;


        internal enum Status
        {
            TIGHT,
            UNDRESS,
            DESTORY,
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

            // ConfigKeyEnableTight = Config.Bind("Tight", "Tight", false, "Cloth tight enabled/disabled");

            // ClothTightStrength = Config.Bind("Tight", "Strength", 1.0f, new ConfigDescription("Set cloth stiffness. higher value is more stiffness", new AcceptableValueRange<float>(-300.0f, 300.0f)));

            ClothMaxDistanceTop = Config.Bind("Undress", "Top", 3.0f, new ConfigDescription("Blow wind upward. higher value is more upward", new AcceptableValueRange<float>(0.0f, 10.0f)));

            ClothMaxDistanceMiddle = Config.Bind("Undress", "Middle", 8.0f, new ConfigDescription("Blow wind upward. higher value is more upward", new AcceptableValueRange<float>(0.0f, 10.0f)));

            ClothMaxDistanceBottom = Config.Bind("Undress", "Bottom", 10.0f, new ConfigDescription("Blow wind upward. higher value is more upward", new AcceptableValueRange<float>(0.0f, 10.0f)));

            ClothAccBottom = Config.Bind("Undress", "Acc", 30.0f, new ConfigDescription("Blow wind upward. higher value is more upward", new AcceptableValueRange<float>(30.0f, 300.0f)));

            ClothDamping = Config.Bind("Undress", "Damping", 0.5f, new ConfigDescription("Blow wind upward. higher value is more upward", new AcceptableValueRange<float>(0.0f, 1.0f)));

            ClothStiffness = Config.Bind("Undress", "Stiffness", 1.0f, new ConfigDescription("Blow wind upward. higher value is more upward", new AcceptableValueRange<float>(0.0f, 10.0f)));


            ConfigKeyEnableUndressShortcut = Config.Bind("ShortKey", "Undress key", new KeyboardShortcut(KeyCode.U));

            // ClothPullDown = Config.Bind("Undress", "Pulldown", 1.0f, new ConfigDescription("Blow wind upward. higher value is more upward", new AcceptableValueRange<float>(0.0f, 300.0f)));

            ClothUndressDuration = Config.Bind("Undress", "Duration", 10.0f, new ConfigDescription("Set undress duration", new AcceptableValueRange<float>(0.0f, 90.0f)));

            ClothMaxDistance = Config.Bind("Undress", "Distance", 0.01f, new ConfigDescription("Set cloth stiffness. higher value is more stiffness", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // ConfigKeyPrintShortcut = Config.Bind("ShortKey", "Print clothes", new KeyboardShortcut(KeyCode.LeftControl, KeyCode.P));


            _self = this;
            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);


            ExtensibleSaveFormat.ExtendedSave.SceneBeingLoaded += OnSceneLoad;

            var harmony = HarmonyExtensions.CreateInstance(GUID);

            harmony.PatchAll(Assembly.GetExecutingAssembly());

            _clothCoroutine = StartCoroutine(ClothRoutine());
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

            if (ConfigKeyEnableUndressShortcut.Value.IsDown())
            {
                if (_selectedOCI != null)
                    _status = Status.UNDRESS;
                else
                    _status = Status.IDLE;
            }

            // if (ConfigKeyPrintShortcut.Value.IsDown())
            // {
            //     GetAllPhysicsClothes();
            // }

            // if (ConfigKeyEnableTight.Value)
            // { 
            //     if (_selectedOCI != null)
            //         _status = Status.TIGHT;
            //     else
            //         _status = Status.IDLE;                
            // }
            // else
            // {
            //     if (_status == Status.TIGHT)
            //         _status = Status.DESTORY;
            //     else
            //         _status = Status.IDLE;
            // }          
        }


        private void OnSceneLoad(string path)
        {
            _status = Status.DESTORY;
            _selectedOCI = null;            
        }

        #endregion

        #region Public Methods

        #endregion

        #region Private Methods
        private void Init()
        {
            UIUtility.Init();
            _loaded = true;
        }
        
        // private void GetAllPhysicsClothes()
        // {
        //     UnityEngine.Debug.Log($">> GetAllPhysicsClothes");
        //     Logger.LogMessage($"GetAllPhysicsClothes ");
        //     string path = Path.Combine(Application.dataPath, "output_log.txt");

        //     if (!File.Exists(path))
        //     {

        //         UnityEngine.Debug.Log($"No file: {path}");
        //         return;
        //     }

        //     foreach (var line in File.ReadLines(path))
        //     {
        //         int idx = line.IndexOf("Loading cloth collider data for");
        //         if (idx >= 0)
        //         {
        //             // 관심 있는 부분만 추출
        //             string part = line.Substring(idx + "Loading cloth collider data for".Length).Trim();

        //             // '.' 로 분리 후 마지막 의미 있는 토큰 가져오기
        //             string[] tokens = part.Split('.');
        //             string name = tokens.Length > 0 ? tokens[tokens.Length - 1] : part;
        //             Logger.LogMessage($"physics clothes -> {name}");
        //         }
        //     }
        // }

        void RestoreMaxDistances()
        {
            if (_selectedOciSmr == null) return;

            Mesh mesh = _selectedOciSmr.sharedMesh;
            if (mesh == null) return;

            foreach (var cloth in _clothes)
            {
                if (cloth == null) continue;

                float[] originalMax = _originalMaxDistances[cloth];

                if (originalMax.Length > 0)
                {
                    Vector3[] vertices = mesh.vertices;
                    ClothSkinningCoefficient[] coeffs = cloth.coefficients;

                    int vertexCount = Mathf.Min(vertices.Length, coeffs.Length);

                    for (int i = 0; i < vertexCount; i++)
                    {
                        coeffs[i].maxDistance = originalMax[i];
                    }
                    cloth.coefficients = coeffs;
                }    
                cloth.useGravity = true;                       
                cloth.externalAcceleration = Vector3.zero;         
            }
        }

        // void ApplyTightening(Cloth cloth)
        // {
        //     Mesh mesh = _selectedOciSmr.sharedMesh;
        //     if (mesh == null) return;

        //     float[] originalMax = _originalMaxDistances[cloth];

        //     Vector3[] vertices = mesh.vertices;
        //     ClothSkinningCoefficient[] coeffs = cloth.coefficients;

        //     int vertexCount = Mathf.Min(vertices.Length, coeffs.Length);

        //     for (int i = 0; i < vertexCount; i++)
        //     {
        //         Vector3 worldPos = _selectedOciSmr.transform.TransformPoint(vertices[i]);
        //         Vector3 localPosRelativeToPelvis = _pelvisBone.InverseTransformPoint(worldPos);

        //         // Z축 기준 앞/뒤 판별
        //         if (ClothTightStrength.Value > 0)
        //         {
        //             if (localPosRelativeToPelvis.z > 0) // 앞쪽
        //             {
        //                 coeffs[i].maxDistance = originalMax[i];                
        //             }
        //             else // 뒤쪽
        //             {
        //                 coeffs[i].maxDistance = 0.0f; // 고정
        //             }
        //         }
        //         else
        //         {
        //             if (localPosRelativeToPelvis.z > 0) // 앞쪽
        //             {
        //                 coeffs[i].maxDistance = 0.0f; // 고정
        //             }
        //             else // 뒤쪽
        //             {
        //                 coeffs[i].maxDistance = originalMax[i];                                             
        //             }                       
        //         }
        //     }

        //     // 바람(앞에서 뒤로) 적용
        //     cloth.externalAcceleration = new Vector3(0f, 0f, -1.5f * ClothTightStrength.Value);

        //     // 변경 적용
        //     cloth.coefficients = coeffs;
        //     cloth.useGravity = false;
        // }

        // void ApplyTighteningAll()
        // {
        //     foreach (var cloth in _clothes)
        //     {
        //         if (cloth == null)
        //             continue;

        //         ApplyTightening(cloth);
        //     }                
        // }

        private IEnumerator UndressClothSegmented(
            Cloth cloth,
            float[] startDistances,
            float duration,
            float topMaxDistance,
            float midMaxDistance,
            float bottomMaxDistance)
        {
            var coeffs = cloth.coefficients;

            float timer = 0f;
            while (timer < duration)
            {
                float t = timer / duration;
                float tSmooth = Mathf.SmoothStep(1f, 1f, t); // 부드럽게 1 → 2 보간

                // 부위별 속도/배율 차이
                float topScale    = Mathf.Lerp(1f, 2f, t); // 상단은 천천히
                float midScale    = Mathf.Lerp(1f, 1f, t); // 허리 부근은 더 천천히
                float bottomScale = Mathf.Lerp(1f, 5f, t); // 하단은 빠르게

                for (int i = 0; i < coeffs.Length; i++)
                {
                    float normalized = (float)i / (coeffs.Length - 1);
                    float targetMaxDistance;

                    if (normalized < 0.80f) // 상단
                        targetMaxDistance = Mathf.Lerp(startDistances[i], topMaxDistance * topScale * tSmooth, t);
                    else if (normalized < 0.90f) // 허리
                        targetMaxDistance = Mathf.Lerp(startDistances[i], midMaxDistance * midScale * tSmooth, t);
                    else // 하단
                        targetMaxDistance = Mathf.Lerp(startDistances[i], bottomMaxDistance * bottomScale * tSmooth, t);

                    coeffs[i].maxDistance = targetMaxDistance;
                }

                cloth.coefficients = coeffs;

                timer += Time.deltaTime;
                yield return null;
            }
        }
                
        private IEnumerator UndressAllBasic(float duration, float topMaxDistance, float middleMaxDistance, float bottomMaxDistance, float externalAcc)
        {
            foreach (var cloth in _clothes)
            {
                if (cloth == null) continue;

                // 물리 안정화
                cloth.damping = ClothDamping.Value;
                cloth.stiffnessFrequency = ClothStiffness.Value;

                // UNDRESS용 아래로 당기는 힘 적용
                cloth.externalAcceleration = new Vector3(0, -1, -0.1f) * externalAcc;

                // MaxDistance 초기값 가져오기
                var coeffs = cloth.coefficients;
                float[] startDistances = new float[coeffs.Length];
                for (int i = 0; i < coeffs.Length; i++)
                    startDistances[i] = coeffs[i].maxDistance;

                yield return StartCoroutine(UndressClothSegmented(cloth, startDistances, duration, topMaxDistance, middleMaxDistance, bottomMaxDistance));
            }
        }

        private IEnumerator ClothRoutine()
        {
            while (true)
            {
                if (_loaded == true)
                {
                    // if (_status == Status.TIGHT)
                    // {
                    //     ApplyTighteningAll();
                    //     yield return new WaitForSeconds(1.0f);
                    // }
                    // else
                    if (_status == Status.UNDRESS)
                    {
                        yield return StartCoroutine(UndressAllBasic(ClothUndressDuration.Value, ClothMaxDistanceTop.Value, ClothMaxDistanceMiddle.Value,  ClothMaxDistanceBottom.Value, ClothAccBottom.Value));
                        RestoreMaxDistances();

                        _status = Status.IDLE;
                    }
                    else if (_status == Status.DESTORY)
                    {
                        _status = Status.IDLE;
                        // 원복                    
                        RestoreMaxDistances();
                        // 제거
                        _clothes.Clear();
                        _originalMaxDistances.Clear();
                    }
                }

                yield return null;
            }
        }
        #endregion

        #region Patches        

        private static Transform GetPelvisBone(SkinnedMeshRenderer smr)
        {
            // // bone 배열에서 hips 찾기
            // foreach (var bone in smr.bones)
            // {
            //     CapsuleCollider[] capsuleColliders = bone.GetComponents<CapsuleCollider>();
            //     SphereCollider[] sphereColliders = bone.GetComponents<SphereCollider>();
            //     BoxCollider[] boxColliders = bone.GetComponents<BoxCollider>();
            //     MeshCollider[] meshColliders = bone.GetComponents<MeshCollider>();

            //     if (bone == null) continue;
            //     string name = bone.name.ToLower();
            // }

            // // 못 찾으면 rootBone 반환 (fallback)
            return smr.rootBone;
        }

        private static SkinnedMeshRenderer GetBodyRenderer(Transform targetTransform)
        {
            SkinnedMeshRenderer bodyRenderer = null;
#if AISHOUJO || HONEYSELECT2
            List<Transform> transformStack = new List<Transform>();

            transformStack.Add(targetTransform);

            while (transformStack.Count != 0)
            {
                Transform currTransform = transformStack[transformStack.Count - 1];
                transformStack.RemoveAt(transformStack.Count - 1);

                if (currTransform.Find("p_cf_body_00"))
                {
                    Transform bodyTransform = currTransform.Find("p_cf_body_00");
                    AIChara.CmpBody bodyCmp = bodyTransform.GetComponent<AIChara.CmpBody>();

                    if (bodyCmp != null)
                    {
                        if (bodyCmp.targetCustom != null && bodyCmp.targetCustom.rendBody != null)
                        {
                            bodyRenderer = bodyCmp.targetCustom.rendBody.transform.GetComponent<SkinnedMeshRenderer>();
                        }
                        else
                        {
                            if (bodyCmp.targetEtc != null && bodyCmp.targetEtc.objBody != null)
                            {
                                bodyRenderer = bodyCmp.targetEtc.objBody.GetComponent<SkinnedMeshRenderer>();
                            }
                        }
                    }

                    break;
                }
                else if(currTransform.Find("p_cm_body_00"))
                {
                    Transform bodyTransform = currTransform.Find("p_cm_body_00");
                    AIChara.CmpBody bodyCmp = bodyTransform.GetComponent<AIChara.CmpBody>();

                    if (bodyCmp != null)
                    {
                        if (bodyCmp.targetCustom != null && bodyCmp.targetCustom.rendBody != null)
                        {
                            bodyRenderer = bodyCmp.targetCustom.rendBody.transform.GetComponent<SkinnedMeshRenderer>();
                        }
                        else
                        {
                            if (bodyCmp.targetEtc != null && bodyCmp.targetEtc.objBody != null)
                            {
                                bodyRenderer = bodyCmp.targetEtc.objBody.GetComponent<SkinnedMeshRenderer>();
                            }
                        }
                    }

                    break;
                }

                for (int i = 0; i < currTransform.childCount; i++)
                {
                    transformStack.Add(currTransform.GetChild(i));
                }
            }
#endif
            return bodyRenderer;
        }

        private static  void CreateSphereDebugWithName(SphereCollider sphere, string name, List<GameObject> debugObjects)
        {
            Camera cam = Camera.main;
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);


            // 부모를 collider의 transform으로 지정
            go.transform.SetParent(sphere.transform, false);

            // localPosition 은 sphere.center 기준
            go.transform.localPosition = sphere.center;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * sphere.radius * 2f;

            // 알파 적용
            Renderer rend = go.GetComponent<Renderer>();            
            SetTransparent(rend, new Color(0f, 1f, 0f, 0.1f)); // 초록 + 알파 0.1

            Destroy(go.GetComponent<Collider>());
            debugObjects.Add(go);

            // 이름 텍스트 (collider 위쪽에 표시)
            Vector3 textPos = sphere.center + Vector3.up * (sphere.radius + 0.05f);
            CreateTextDebugLocal(sphere.transform, textPos, name, cam, debugObjects);
        }

        private static void CreateCapsuleDebugWithName(CapsuleCollider capsule, string name, List<GameObject> debugObjects)
        {
            Camera cam = Camera.main;

            // 부모 기준으로 생성
            GameObject root = new GameObject(capsule.name + "_CapsuleDebug");
            root.transform.SetParent(capsule.transform, false);
            root.transform.localPosition = capsule.center;
            root.transform.localRotation = Quaternion.identity;

            // 캡슐은 Unity Primitive 로는 직접 만들기 어려워서,
            // 양쪽 Sphere + Cylinder 조합으로 표시
            GameObject topSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            topSphere.transform.SetParent(root.transform, false);
            topSphere.transform.localPosition = Vector3.up * (capsule.height * 0.5f - capsule.radius);
            topSphere.transform.localScale = Vector3.one * capsule.radius * 2f;
            topSphere.GetComponent<Renderer>().material.color = Color.green;
            Destroy(topSphere.GetComponent<Collider>());
            debugObjects.Add(topSphere);

            GameObject bottomSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bottomSphere.transform.SetParent(root.transform, false);
            bottomSphere.transform.localPosition = Vector3.down * (capsule.height * 0.5f - capsule.radius);
            bottomSphere.transform.localScale = Vector3.one * capsule.radius * 2f;
            bottomSphere.GetComponent<Renderer>().material.color = Color.green;
            Destroy(bottomSphere.GetComponent<Collider>());
            debugObjects.Add(bottomSphere);

            // 가운데 Cylinder 부분
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = new Vector3(capsule.radius * 2f, (capsule.height * 0.5f - capsule.radius), capsule.radius * 2f);

            // 알파 적용
            Renderer rend = body.GetComponent<Renderer>();
            SetTransparent(rend, new Color(0f, 1f, 0f, 0.1f)); // 초록 + 알파 0.1
 
            Destroy(body.GetComponent<Collider>());
            debugObjects.Add(body);

            // 이름 텍스트
            Vector3 textPos = Vector3.up * (capsule.height * 0.5f + 0.1f);
            CreateTextDebugLocal(root.transform, textPos, name, cam, debugObjects);
        }

        // BoxCollider 디버그
        private static void CreateBoxDebugWithName(BoxCollider box, string name, List<GameObject> debugObjects)
        {
            Camera cam = Camera.main;

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(box.transform, false);
            go.transform.localPosition = box.center;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = box.size;

            // 알파 적용
            Renderer rend = go.GetComponent<Renderer>();
            SetTransparent(rend, new Color(0f, 1f, 0f, 0.1f)); // 초록 + 알파 0.1
            
            Destroy(go.GetComponent<Collider>());
            debugObjects.Add(go);

            // 텍스트 (박스 위)
            Vector3 textPos = box.center + Vector3.up * (Mathf.Max(box.size.x, box.size.y, box.size.z) * 0.5f + 0.1f);
            CreateTextDebugLocal(box.transform, textPos, name, cam, debugObjects);
        }

        private static void CreateTextDebugLocal(Transform parent, Vector3 localPos, string text, Camera cam, List<GameObject> debugObjects)
        {
            GameObject textObj = new GameObject("ColliderName");
            textObj.transform.SetParent(parent, false);
            textObj.transform.localPosition = localPos;

            TextMesh tm = textObj.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 30;
            tm.color = Color.red;
            tm.characterSize = 0.05f;
            tm.anchor = TextAnchor.MiddleCenter;

            // 카메라를 바라보도록
            if (cam != null)
                textObj.transform.rotation = Quaternion.LookRotation(textObj.transform.position - cam.transform.position);

            debugObjects.Add(textObj);
        }

        private static void SetTransparent(Renderer rend, Color color)
        {
            Material mat = new Material(Shader.Find("Standard"));

            // Rendering Mode = Transparent
            mat.SetFloat("_Mode", 3); // 3 = Transparent
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3100;

            // 색상 + 알파 적용
            mat.color = color;

            // Renderer에 적용
            rend.material = mat;
        }

        private static void ProcessDynamicBones(ObjectCtrlInfo objectCtrlInfo)
        {
            if (objectCtrlInfo != null)
            {

                _self._selectedOCI = objectCtrlInfo;
                _self._selectedOciSmr = GetBodyRenderer(_self._selectedOCI.guideObject.transformTarget);
                _self._pelvisBone = GetPelvisBone(_self._selectedOciSmr);

                _self._clothes.Clear();
                _self._originalMaxDistances.Clear();

                // OCIChar character = _self._selectedOCI as OCIChar;
                // ChaControl chaControl = character.charInfo;

                // var clotheObjs = chaControl.objClothes;

                // var clothParts = charFemale.nowCoordinate.clothes.parts;
                List<GameObject> debugObjects = new List<GameObject>();

                foreach (var bone in _self._selectedOciSmr.bones)
                {
                    // SphereCollider[] colliders = bone.GetComponentsInChildren<SphereCollider>();
                    // // UnityEngine.Debug.Log($">> found collider bone  {bone.name}, {colliders.Length}");

                    // foreach (var col in colliders)
                    // {
                    //     CreateSphereDebugWithName(col, bone.name, debugObjects);
                    // }

                    CapsuleCollider[] colliders = bone.GetComponentsInChildren<CapsuleCollider>();

                    foreach (var col in colliders)
                    {
                        CreateCapsuleDebugWithName(col, bone.name, debugObjects);
                    }                    
                }

                Cloth[] cloths = _self._selectedOCI.guideObject.transformTarget.GetComponentsInChildren<Cloth>(true);
                foreach (var cloth in cloths)
                {
                    if (cloth == null || cloth.transform == null)
                        continue;

                    _self._clothes.Add(cloth);

                    cloth.stretchingStiffness = 0.5f; // 기본값보다 낮춤
                    cloth.damping = 1.0f; // 마찰 줄여서 더 잘 내려가게                    

                    // Max Distance 처리
                    ClothSkinningCoefficient[] coeffs = cloth.coefficients;
                    float[] maxDistances = new float[coeffs.Length];

                    for (int i = 0; i < coeffs.Length; i++)
                        maxDistances[i] = coeffs[i].maxDistance;

                    _self._originalMaxDistances.Add(cloth, maxDistances);

                    // CapsuleCollider 추가 
                    // 기존 capsuleColliders 배열을 List로 변환
                    // var list = new List<CapsuleCollider>(cloth.capsuleColliders ?? new CapsuleCollider[0]);

                    // // 중복 방지: 이미 등록되어 있으면 추가하지 않음
                    // if (!list.Contains(col))
                    // {
                    //     list.Add(col);
                    //     cloth.capsuleColliders = list.ToArray();
                    // }
                }
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnSelectSingle_Patches
        {

            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                ObjectCtrlInfo objectCtrlInfo = null;

                if (Singleton<Studio.Studio>.Instance.dicInfo.TryGetValue(_node, out objectCtrlInfo))
                {
                    ProcessDynamicBones(objectCtrlInfo as OCIChar);
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectMultiple))]
        private static class WorkspaceCtrl_OnSelectMultiple_Patches
        {
            private static bool Prefix(object __instance)
            {

                TreeNodeObject _node = Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes[0];

                ObjectCtrlInfo objectCtrlInfo = null;

                if (Singleton<Studio.Studio>.Instance.dicInfo.TryGetValue(_node, out objectCtrlInfo))
                {
                    ProcessDynamicBones(objectCtrlInfo as OCIChar);
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeselectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnDeselectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                if (Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes.Count() == 0)
                {
                    _self._status = Status.DESTORY;
                    _self._selectedOCI = null;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(OCIChar), "ChangeChara", new[] { typeof(string) })]
        internal static class OCIChar_ChangeChara_Patches
        {
            public static void Postfix(OCIChar __instance, string _path)
            {
                ProcessDynamicBones(__instance as OCIChar);
            }
        }
        
        // [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.ChangeCustomClothes), typeof(int), typeof(bool), typeof(bool), typeof(bool), typeof(bool))]        
        // internal static class ChaControl_ChangeCustomClothes_Patches
        // {
        //     public static void Postfix(ChaControl __instance, int kind, bool updateColor, bool updateTex01, bool updateTex02, bool updateTex03)
        //     {
        //         // UnityEngine.Debug.Log($">> ChangeCustomClothes {kind}");
        //         if (__instance != null)
        //         {
        //             __instance.StartCoroutine(ExecuteAfterFrame(__instance.GetOCIChar() as ObjectCtrlInfo));
        //         }
        //     }           
        // }

        [HarmonyPatch(typeof(Studio.Studio), "InitScene", typeof(bool))]
        private static class Studio_InitScene_Patches
        {
            private static bool Prefix(object __instance, bool _close)
            {
                _self._status = Status.DESTORY;
                _self._selectedOCI = null;

                return true;
            }
        }        

        #endregion
    }
}
