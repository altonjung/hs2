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
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

#if AISHOUJO || HONEYSELECT2
using CharaUtils;
using ExtensibleSaveFormat;
using AIChara;
using System.Security.Cryptography;
using KKAPI.Studio;
using IllusionUtility.GetUtility;
#endif


namespace WindPhysics
{
    public class Logic
    {  
        internal static WindData CreateWindData(ObjectCtrlInfo ociChar)
        {
            WindData windData = new WindData();
            windData.objectCtrlInfo = ociChar;

            return windData;
        }

        internal static IEnumerator ExecuteDynamicBoneAfterFrame(WindData windData)
        {
            int frameCount = 20;
            for (int i = 0; i < frameCount; i++)
                yield return null;

            ReallocateDynamicBones(windData);
        }

        internal static void StopUnselectedCtrl()
        {
            foreach (ObjectCtrlInfo ctrlInfo in WindPhysics._self._selectedOCIs)
            {   

                OCIChar selectedOciChar = ctrlInfo as OCIChar; 
                OCIItem selectedOciItem = ctrlInfo as OCIItem;  
                
                if (WindPhysics._self._ociObjectMgmt.TryGetValue(ctrlInfo.GetHashCode(), out var windData))
                {
                    if (windData.coroutine != null)
                    {
                        windData.wind_status = Status.STOP;
                    }
                }
#if FEATURE_SUPPORT_ITEM
                if (_self._ociObjectMgmt.TryGetValue(ctrlInfo.GetHashCode(), out var windData))
                {
                    if (windData.coroutine != null)
                    {
                        windData.wind_status = Status.STOP;
                    }
                }
#endif
            }

            WindPhysics._self._selectedOCIs.Clear();
        }

        internal static void ReallocateDynamicBones(WindData windData)
        {
            windData.wind_status = WindPhysics.ConfigKeyEnableWind.Value ? Status.RUN : Status.DESTROY;

            if (windData.objectCtrlInfo != null)
            {
                OCIChar ociChar = windData.objectCtrlInfo as OCIChar;
                OCIItem ociItem = windData.objectCtrlInfo as OCIItem;

                // 기존 자원 제거
                WindPhysics._self.ClearWind(windData);

                if (ociChar != null) {
                    ChaControl baseCharControl = ociChar.charInfo;

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
                }

                if (ociItem != null) {                    
                    DynamicBone[] bones = ociItem.guideObject.transformTarget.gameObject.GetComponentsInChildren<DynamicBone>(true);
                    Cloth[] clothes = ociItem.guideObject.transformTarget.gameObject.GetComponentsInChildren<Cloth>(true);

                    windData.accesoriesDynamicBones = bones.ToList();
                    windData.clothes = clothes.ToList();
                }               

                if (windData.clothes.Count != 0 || windData.hairDynamicBones.Count != 0 || windData.accesoriesDynamicBones.Count != 0)
                {
                    WindPhysics._self._selectedOCIs.Add(windData.objectCtrlInfo);
                    WindPhysics._self._ociObjectMgmt.Add(windData.objectCtrlInfo.GetHashCode(), windData);

                    // Coroutine
                    if (ociChar != null) {
                            windData.coroutine = WindPhysics.ConfigKeyEnableWind.Value ? ociChar.charInfo.StartCoroutine(WindPhysics._self.WindRoutine(windData)) : null;  
                    }
#if FEATURE_SUPPORT_ITEM
                    if (ociItem != null) {
                        windData.coroutine = ConfigKeyEnableWind.Value ? ociItem.guideObject.StartCoroutine(_self.WindRoutine(windData)) : null; 
                    }
#endif
                }
            }
        }

        internal static void TryAllocateObject(List<ObjectCtrlInfo> curObjCtrlInfos) {

            StopUnselectedCtrl();
            WindPhysics._self._selectedOCIs.Clear();

            foreach (ObjectCtrlInfo ctrlInfo in curObjCtrlInfos)
            {
                if (ctrlInfo != null)
                {
                    OCIChar ociChar = ctrlInfo as OCIChar;
                    OCIItem ociItem = ctrlInfo as OCIItem;
                    if (ociChar != null)
                    {
                        if (WindPhysics._self._ociObjectMgmt.TryGetValue(ociChar.GetHashCode(), out var windData1))
                        {
                            if (windData1.wind_status == Status.RUN || windData1.wind_status == Status.STOP || windData1.wind_status == Status.IDLE)
                            {
                                windData1.wind_status = Status.RUN;                            
                            } 
                            else
                            {
                                ociChar.GetChaControl().StartCoroutine(ExecuteDynamicBoneAfterFrame(windData1));
                            }
                        }
                        else
                        {
                            WindData windData2 = CreateWindData(ociChar);
                            ociChar.GetChaControl().StartCoroutine(ExecuteDynamicBoneAfterFrame(windData2));
                        }                      
                    }         

                    if (ociItem != null)
                    {
    #if FEATURE_SUPPORT_ITEM
                        if (_self._ociObjectMgmt.TryGetValue(ociItem.GetHashCode(), out var windData1))
                        {
                            if (windData1.wind_status == Status.RUN || windData1.wind_status == Status.STOP || windData1.wind_status == Status.IDLE)
                            {
                                windData1.wind_status = Status.RUN;                            
                            } 
                            else
                            {
                                ociItem.guideObject.StartCoroutine(ExecuteDynamicBoneAfterFrame(windData1));
                            }
                        }
                        else
                        {
                            WindData windData2 = CreateWindData(ociItem);
                            ociItem.guideObject.StartCoroutine(ExecuteDynamicBoneAfterFrame(windData2));
                        } 
    #endif                    
                    }
                }        
            }    
        }
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