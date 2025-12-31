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
        #region Private Methods            
        internal static void SetCharacter(OCIChar ociChar)
        {
            if (ociChar != null) {
                ChaControl baseCharControl = ociChar.charInfo;
                
                // Hair
                List<DynamicBone> hairDynamicBones = new List<DynamicBone>();
                List<DynamicBone> accesoriesDynamicBones = new List<DynamicBone>();
                List<Cloth> clothes = new List<Cloth>();

                hairDynamicBones = baseCharControl.objBodyBone.transform.FindLoop("cf_J_Head").GetComponentsInChildren<DynamicBone>(true).ToList();

                // Accesories
                foreach (var accessory in baseCharControl.objAccessory)
                {
                    if (accessory != null && accessory.GetComponentsInChildren<DynamicBone>().Length > 0)
                    {
                        accesoriesDynamicBones.Add(accessory.GetComponentsInChildren<DynamicBone>()[0]);
                    }
                }

                // Cloth
                clothes = baseCharControl.transform.GetComponentsInChildren<Cloth>(true).ToList();
                                
                // setting
                foreach (DynamicBone bone in hairDynamicBones) {
                    if (bone == null)
                        continue;                    
                    bone.m_Damping = WindPhysics.HairDamping.Value;
                    bone.m_Stiffness = WindPhysics.HairStiffness.Value;
                    //bone.m_Force = WindPhysics.HairForce.Value;
                    bone.m_Gravity = new Vector3(0, UnityEngine.Random.Range(-0.005f, -0.01f), 0); // 아래 방향 중력
                }
                
                foreach (DynamicBone bone in accesoriesDynamicBones) {
                    if (bone == null)
                        continue;
                    bone.m_Damping = WindPhysics.AccesoriesDamping.Value;
                    bone.m_Stiffness = WindPhysics.AccesoriesStiffness.Value;
                    //bone.m_Force = WindPhysics.AccesoriesForce.Value;
                    bone.m_Gravity = new Vector3(0, UnityEngine.Random.Range(-0.01f, -0.03f), 0); // 아래 방향 중력
                }

                foreach (Cloth cloth in clothes) {
                    if (cloth == null)
                        continue;                    
                    cloth.useGravity = true;
                    cloth.worldAccelerationScale = 0.5f; // 외부 가속도 반영 비율
                    cloth.worldVelocityScale = 0.5f;
                    cloth.randomAcceleration = Vector3.zero;
                    cloth.damping = WindPhysics.ClothDamping.Value;
                    cloth.stiffnessFrequency = WindPhysics.ClothStiffness.Value;
                    cloth.externalAcceleration = Vector3.zero;
                }
            }            
        }

        internal static void SetItem(OCIItem ociItem)
        {
            if (ociItem != null) {
                DynamicBone[] bones = ociItem.guideObject.transformTarget.gameObject.GetComponentsInChildren<DynamicBone>(true);
                Cloth[] clothes = ociItem.guideObject.transformTarget.gameObject.GetComponentsInChildren<Cloth>(true);

                foreach (DynamicBone bone in bones) {
                    if (bone == null)
                        continue;
                    bone.m_Damping = WindPhysics.ItemDamping.Value;
                    bone.m_Stiffness = WindPhysics.ItemStiffness.Value;
                    //bone.m_Force = WindPhysics.ItemForce.Value;
                    bone.m_Gravity = new Vector3(0, UnityEngine.Random.Range(-0.01f, -0.05f), 0); // 아래 방향 중력
                }

                foreach (Cloth cloth in clothes) {
                    if (cloth == null)
                        continue;                    
                    cloth.useGravity = true;
                    cloth.worldAccelerationScale = 0.8f; // 외부 가속도 반영 비율
                    cloth.worldVelocityScale = 0.8f;
                    cloth.randomAcceleration = Vector3.zero;
                    cloth.damping = WindPhysics.ItemDamping.Value;
                    cloth.stiffnessFrequency = WindPhysics.ItemStiffness.Value;
                    cloth.externalAcceleration = Vector3.zero;
                }                      
            }               
        }      
        #endregion
    }   
}