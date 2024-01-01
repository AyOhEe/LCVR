﻿using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using System;
using UnityEngine.Rendering.HighDefinition;
using LCVR.Input;
using UnityEngine.Animations.Rigging;
using System.Collections;
using LCVR.Networking;
using LCVR.Assets;
using GameNetcodeStuff;
using UnityEngine.XR.Interaction.Toolkit;
using Microsoft.MixedReality.Toolkit.Experimental.UI;

namespace LCVR.Player
{
    // Attach this to the main Player

    internal class VRPlayer : MonoBehaviour
    {
        public static VRPlayer Instance { get; private set; }

        public float scaleFactor = 1.5f;
        private float cameraFloorOffset = 0f;
        private float crouchOffset = 0f;

        private bool wasInSpecialAnimation = false;
        private Vector3 specialAnimationPositionOffset = Vector3.zero;

        private PlayerControllerB playerController;
        
        public GameObject leftController;
        public GameObject rightController;

        private GameObject leftControllerRayInteractor;
        private GameObject rightControllerRayInteractor;

        private GameObject xrOrigin;

        private Vector3 lastFrameHMDPosition = new(0, 0, 0);
        private Vector3 lastFrameHMDRotation = new(0, 0, 0);

        private TurningProvider turningProvider;

        public VRHUD hud;
        public VRController mainHand;
        public Camera mainCamera;
        public Camera customCamera;
        public Camera uiCamera;

        public Transform leftHandRigTransform;
        public Transform rightHandRigTransform;

        private GameObject leftHandVRTarget;
        private GameObject rightHandVRTarget;

        public Transform leftItemHolder;
        public Transform rightItemHolder;

        private void Awake()
        {
            Instance = this;

            Logger.LogDebug("Going to intialize XR Rig");

            playerController = GetComponent<PlayerControllerB>();
            
            // Create XR stuff
            xrOrigin = new GameObject("XR Origin");
            mainCamera = Find("ScavengerModel/metarig/CameraContainer/MainCamera").GetComponent<Camera>();
            uiCamera = GameObject.Find("UICamera").GetComponent<Camera>();

            if (Plugin.Config.EnableCustomCamera.Value)
                customCamera = mainCamera.gameObject.Find("Custom Camera").GetComponent<Camera>();

            // Fool the animator (this removes console error spam)
            new GameObject("MainCamera").transform.parent = Find("ScavengerModel/metarig/CameraContainer").transform;

            // Unparent camera container
            mainCamera.transform.parent = xrOrigin.transform;
            xrOrigin.transform.localPosition = Vector3.zero;
            xrOrigin.transform.localRotation = Quaternion.Euler(0, 0, 0);
            xrOrigin.transform.localScale = Vector3.one;

            // Create HMD tracker
            var cameraPoseDriver = mainCamera.gameObject.AddComponent<TrackedPoseDriver>();
            cameraPoseDriver.positionAction = Actions.XR_HeadPosition;
            cameraPoseDriver.rotationAction = Actions.XR_HeadRotation;
            cameraPoseDriver.trackingStateInput = new InputActionProperty(Actions.XR_HeadTrackingState);

            // Create controller objects
            rightController = new GameObject("Right Controller");
            leftController = new GameObject("Left Controller");

            // And mount to camera container
            rightController.transform.parent = xrOrigin.transform;
            leftController.transform.parent = xrOrigin.transform;

            // Left hand tracking
            var rightHandPoseDriver = rightController.AddComponent<TrackedPoseDriver>();
            rightHandPoseDriver.positionAction = Actions.XR_RightHand_Position;
            rightHandPoseDriver.rotationAction = Actions.XR_RightHand_Rotation;
            rightHandPoseDriver.trackingStateInput = new InputActionProperty(Actions.XR_RightHand_TrackingState);

            // Right hand tracking
            var leftHandPoseDriver = leftController.AddComponent<TrackedPoseDriver>();
            leftHandPoseDriver.positionAction = Actions.XR_LeftHand_Position;
            leftHandPoseDriver.rotationAction = Actions.XR_LeftHand_Rotation;
            leftHandPoseDriver.trackingStateInput = new InputActionProperty(Actions.XR_LeftHand_TrackingState);

            // Set up IK Rig VR targets
            var headVRTarget = new GameObject("Head VR Target");
            rightHandVRTarget = new GameObject("Right Hand VR Target");
            leftHandVRTarget = new GameObject("Left Hand VR Target");

            headVRTarget.transform.parent = mainCamera.transform;
            rightHandVRTarget.transform.parent = rightController.transform;
            leftHandVRTarget.transform.parent = leftController.transform;

            // Head defintely does need to have offsets (in this case an offset of 0, 0, 0)
            headVRTarget.transform.localPosition = Vector3.zero;

            rightHandVRTarget.transform.localPosition = new Vector3(0.0279f, 0.0353f, -0.0044f);
            rightHandVRTarget.transform.localRotation = Quaternion.Euler(0, 90, 168);

            leftHandVRTarget.transform.localPosition = new Vector3(-0.0279f, 0.0353f, 0.0044f);
            leftHandVRTarget.transform.localRotation = Quaternion.Euler(0, 270, 192);

            // ARMS ONLY RIG

            // Set up rigging
            var model = Find("ScavengerModel/metarig/ScavengerModelArmsOnly", true).gameObject;
            var modelMetarig = Find("ScavengerModel/metarig/ScavengerModelArmsOnly/metarig", true);

            Find("ScavengerModel/metarig/ScavengerModelArmsOnly/metarig/spine.003/RigArms", true);

            var rigFollow = model.AddComponent<IKRigFollowVRRig>();

            // Setting up the head
            rigFollow.head = mainCamera.transform;

            // Setting up the right arm

            rightHandRigTransform = Find("ScavengerModel/metarig/ScavengerModelArmsOnly/metarig/spine.003/shoulder.R/arm.R_upper/arm.R_lower/hand.R").transform;

            var rightArmTarget = Find("ScavengerModel/metarig/ScavengerModelArmsOnly/metarig/spine.003/RigArms/RightArm/ArmsRightArm_target");
            var rightArmHint = Find("ScavengerModel/metarig/ScavengerModelArmsOnly/metarig/spine.003/RigArms/RightArm/RightArm_hint");

            rightArmHint.transform.localPosition = new Vector3(12.5f, -2f, -1f);

            rigFollow.rightHand = new IKRigFollowVRRig.VRMap()
            {
                ikTarget = rightArmTarget.transform,
                vrTarget = rightHandVRTarget.transform,
                trackingPositionOffset = Vector3.zero,
                trackingRotationOffset = Vector3.zero
            };

            // Setting up the left arm

            leftHandRigTransform = Find("ScavengerModel/metarig/ScavengerModelArmsOnly/metarig/spine.003/shoulder.L/arm.L_upper/arm.L_lower/hand.L").transform;

            var leftArmTarget = Find("ScavengerModel/metarig/ScavengerModelArmsOnly/metarig/spine.003/RigArms/LeftArm/ArmsLeftArm_target");
            var leftArmHint = Find("ScavengerModel/metarig/ScavengerModelArmsOnly/metarig/spine.003/RigArms/LeftArm/LeftArm_hint");

            leftArmHint.transform.localPosition = new Vector3(-10f, -2f, -1f);

            rigFollow.leftHand = new IKRigFollowVRRig.VRMap()
            {
                ikTarget = leftArmTarget.transform,
                vrTarget = leftHandVRTarget.transform,
                trackingPositionOffset = Vector3.zero,
                trackingRotationOffset = Vector3.zero
            };

            // This one is pretty hit or miss, sometimes y needs to be -0.2f, other times it needs to be -2.25f
            rigFollow.headBodyPositionOffset = new Vector3(0, -0.2f, 0);

            // Disable badges
            Find("ScavengerModel/metarig/spine/spine.001/spine.002/spine.003/LevelSticker").gameObject.SetActive(false);
            Find("ScavengerModel/metarig/spine/spine.001/spine.002/spine.003/BetaBadge").gameObject.SetActive(false);

            // FULL BODY RIG

            // Set up rigging
            var fullModel = Find("ScavengerModel", true).gameObject;
            var fullModelMetarig = Find("ScavengerModel/metarig", true);

            var fullRigFollow = fullModel.AddComponent<IKRigFollowVRRig>();

            // Setting up the right arm

            var fullRightArmTarget = Find("ScavengerModel/metarig/spine/spine.001/spine.002/spine.003/RightArm_target");
            var fullRightArmHint = Find("ScavengerModel/metarig/Rig 1/RightArm/RightArm_hint");

            fullRightArmHint.transform.localPosition = new Vector3(12.5f, -2f, -1f);

            fullRigFollow.rightHand = new IKRigFollowVRRig.VRMap()
            {
                ikTarget = fullRightArmTarget.transform,
                vrTarget = rightHandVRTarget.transform,
                trackingPositionOffset = Vector3.zero,
                trackingRotationOffset = Vector3.zero
            };

            // Setting up the left arm

            var fullLeftArmTarget = Find("ScavengerModel/metarig/spine/spine.001/spine.002/spine.003/LeftArm_target");
            var fullLeftArmHint = Find("ScavengerModel/metarig/Rig 1/LeftArm/LeftArm_hint");

            fullLeftArmHint.transform.localPosition = new Vector3(-10f, -2f, -1f);

            fullRigFollow.leftHand = new IKRigFollowVRRig.VRMap()
            {
                ikTarget = fullLeftArmTarget.transform,
                vrTarget = leftHandVRTarget.transform,
                trackingPositionOffset = Vector3.zero,
                trackingRotationOffset = Vector3.zero
            };

            // This one is pretty hit or miss, sometimes y needs to be 0, other times it needs to be -2.25f
            rigFollow.headBodyPositionOffset = new Vector3(0, 0, 0);

            // Add controller interactor
            mainHand = rightController.AddComponent<VRController>();
            mainHand.Initialize(this);

            // Add ray interactors for VR keyboard
            leftControllerRayInteractor = AddRayInteractor(leftController.transform, "LeftHand");
            rightControllerRayInteractor = AddRayInteractor(rightController.transform, "RightHand");

            leftControllerRayInteractor.transform.localPosition = new Vector3(0.01f, 0, 0);
            leftControllerRayInteractor.transform.localRotation = Quaternion.Euler(80, 0, 0);

            rightControllerRayInteractor.transform.localPosition = new Vector3(-0.01f, 0, 0);
            rightControllerRayInteractor.transform.localRotation = Quaternion.Euler(80, 0, 0);

            // Add turning provider
            turningProvider = Plugin.Config.TurnProvider switch
            {
                Config.TurnProviderOption.Snap => new SnapTurningProvider(),
                Config.TurnProviderOption.Smooth => new SmoothTurningProvider(),
                _ => new NullTurningProvider(),
            };

            // Rebuild rig
            GetComponentInChildren<RigBuilder>().Build();
            ResetHeight();

            // Set up item holders
            var rightHandTarget = Find("ScavengerModel/metarig/ScavengerModelArmsOnly/metarig/spine.003/shoulder.R/arm.R_upper/arm.R_lower/hand.R");
            var leftHandTarget = Find("ScavengerModel/metarig/ScavengerModelArmsOnly/metarig/spine.003/shoulder.L/arm.L_upper/arm.L_lower/hand.L");

            var rightHolder = new GameObject("Right Hand Item Holder");
            var leftHolder = new GameObject("Left Hand Item Holder");

            rightItemHolder = rightHolder.transform;
            rightItemHolder.SetParent(rightHandTarget, false);
            rightItemHolder.localPosition = new Vector3(-0.002f, 0.036f, -0.042f);
            rightItemHolder.localEulerAngles = new Vector3(356.3837f, 357.6979f, 0.1453f);

            leftItemHolder = leftHolder.transform;
            leftItemHolder.SetParent(leftHandTarget, false);
            leftItemHolder.localPosition = new Vector3(0.018f, 0.045f, -0.042f);
            leftItemHolder.localEulerAngles = new Vector3(360f - 356.3837f, 357.6979f, 0.1453f);

            Logger.LogDebug("Initialized XR Rig");
        }

        private GameObject AddRayInteractor(Transform parent, string hand)
        {
            var @object = new GameObject($"{hand} Ray Interactor");
            @object.transform.SetParent(parent, false);

            var controller = @object.AddComponent<ActionBasedController>();
            var interactor = @object.AddComponent<XRRayInteractor>();
            var visual = @object.AddComponent<XRInteractorLineVisual>();
            var renderer = @object.GetComponent<LineRenderer>();

            interactor.raycastMask = LayerMask.GetMask("UI");

            visual.lineBendRatio = 1;
            visual.invalidColorGradient = new Gradient()
            {
                mode = GradientMode.Blend,
                alphaKeys = [
                    new GradientAlphaKey(1, 0),
                    new GradientAlphaKey(1, 1)
                ],
                colorKeys = [
                    new GradientColorKey(Color.gray, 0),
                    new GradientColorKey(Color.gray, 1)
                ]
            };
            visual.enabled = false;

            renderer.material = AssetManager.defaultRayMat;

            controller.enableInputTracking = false;
            controller.selectAction = new InputActionProperty(AssetManager.defaultInputActions.FindAction($"{hand}/Select"));
            controller.selectActionValue = new InputActionProperty(AssetManager.defaultInputActions.FindAction($"{hand}/Select Value"));
            controller.activateAction = new InputActionProperty(AssetManager.defaultInputActions.FindAction($"{hand}/Activate"));
            controller.activateActionValue = new InputActionProperty(AssetManager.defaultInputActions.FindAction($"{hand}/Activate Value"));
            controller.uiPressAction = new InputActionProperty(AssetManager.defaultInputActions.FindAction($"{hand}/UI Press"));
            controller.uiPressActionValue = new InputActionProperty(AssetManager.defaultInputActions.FindAction($"{hand}/UI Press Value"));
            controller.uiScrollAction = new InputActionProperty(AssetManager.defaultInputActions.FindAction($"{hand}/UI Scroll"));
            controller.rotateAnchorAction = new InputActionProperty(AssetManager.defaultInputActions.FindAction($"{hand}/Rotate Anchor"));
            controller.translateAnchorAction = new InputActionProperty(AssetManager.defaultInputActions.FindAction($"{hand}/Translate Anchor"));
            controller.scaleToggleAction = new InputActionProperty(AssetManager.defaultInputActions.FindAction($"{hand}/Scale Toggle"));
            controller.scaleDeltaAction = new InputActionProperty(AssetManager.defaultInputActions.FindAction($"{hand}/Scale Delta"));

            return @object;
        }

        private void Update()
        {
            var movement = mainCamera.transform.localPosition - lastFrameHMDPosition;
            movement.y = 0;

            var rotationOffset = Quaternion.Euler(0, turningProvider.GetRotationOffset(), 0);

            var movementAccounted = rotationOffset * movement;
            var cameraPosAccounted = rotationOffset * new Vector3(mainCamera.transform.localPosition.x, 0, mainCamera.transform.localPosition.z);

            if (!wasInSpecialAnimation && playerController.inSpecialInteractAnimation)
                specialAnimationPositionOffset = new Vector3(-cameraPosAccounted.x * scaleFactor, 0, -cameraPosAccounted.z * scaleFactor);

            wasInSpecialAnimation = playerController.inSpecialInteractAnimation;

            // Move player if we're not in special interact animation
            if (!playerController.inSpecialInteractAnimation)
                transform.position += new Vector3(movementAccounted.x * scaleFactor, 0, movementAccounted.z * scaleFactor);

            // Update rotation offset after adding movement from frame
            turningProvider.Update();

            // If we are in special animation allow 6 DOF but don't update player position
            if (!playerController.inSpecialInteractAnimation)
                xrOrigin.transform.position = new Vector3(transform.position.x - cameraPosAccounted.x * scaleFactor, transform.position.y, transform.position.z - cameraPosAccounted.z * scaleFactor);
            else
                xrOrigin.transform.position = transform.position + specialAnimationPositionOffset;

            // Apply crouch offset
            crouchOffset = Mathf.Lerp(crouchOffset, playerController.isCrouching ? -1 : 0, 0.1f);

            // Apply floor offset and sinking value
            xrOrigin.transform.position += new Vector3(0, cameraFloorOffset + crouchOffset - playerController.sinkingValue * 2.5f, 0);
            xrOrigin.transform.rotation = rotationOffset;
            xrOrigin.transform.localScale = Vector3.one * scaleFactor;

            if (!playerController.inSpecialInteractAnimation)
                transform.rotation = Quaternion.Euler(transform.rotation.eulerAngles.x, mainCamera.transform.eulerAngles.y, transform.rotation.eulerAngles.z);

            if (!playerController.inSpecialInteractAnimation)
                lastFrameHMDPosition = mainCamera.transform.localPosition;

            DNet.BroadcastRig(new DNet.Rig()
            {
                leftHandPosition = leftHandVRTarget.transform.position,
                leftHandEulers = leftHandVRTarget.transform.eulerAngles,

                rightHandPosition = rightHandVRTarget.transform.position,
                rightHandEulers = rightHandVRTarget.transform.eulerAngles,

                cameraEulers = mainCamera.transform.eulerAngles,
            });
        }

        private void LateUpdate()
        {
            var angles = mainCamera.transform.eulerAngles;
            StartOfRound.Instance.playerLookMagnitudeThisFrame = (angles - lastFrameHMDRotation).magnitude * Time.deltaTime;

            lastFrameHMDRotation = angles;
        }

        public void OnDeath()
        {
            VRPlayer.VibrateController(XRNode.LeftHand, 1f, 1f);
            VRPlayer.VibrateController(XRNode.RightHand, 1f, 1f);

            if (Plugin.Config.EnableCustomCamera.Value)
                customCamera.enabled = false;

            var uiCameraAnchor = GameObject.Find("VR UI Camera Anchor") ?? new GameObject("VR UI Camera Anchor");
            uiCameraAnchor.transform.position = new Vector3(0, -1000, 0);

            var hdUICamera = uiCamera.GetComponent<HDAdditionalCameraData>();
            var hdMainCamera = mainCamera.GetComponent<HDAdditionalCameraData>();

            hdMainCamera.xrRendering = false;
            mainCamera.stereoTargetEye = StereoTargetEyeMask.None;
            mainCamera.depth = uiCamera.depth - 1;
            mainCamera.enabled = false;

            hdUICamera.xrRendering = true;
            uiCamera.stereoTargetEye = StereoTargetEyeMask.Both;
            uiCamera.transform.SetParent(uiCameraAnchor.transform, false);
            uiCamera.nearClipPlane = 0.01f;
            uiCamera.farClipPlane = 15f;
            uiCamera.enabled = true;

            var poseDriver = uiCamera.GetComponent<TrackedPoseDriver>() ?? uiCamera.gameObject.AddComponent<TrackedPoseDriver>();
            poseDriver.positionAction = Actions.XR_HeadPosition;
            poseDriver.rotationAction = Actions.XR_HeadRotation;
            poseDriver.trackingStateInput = new InputActionProperty(Actions.XR_HeadTrackingState);

            hud.UpdateHUDForSpectatorCam();
        }

        public void OnRevive()
        {
            if (Plugin.Config.EnableCustomCamera.Value)
                customCamera.enabled = true;

            var hdUICamera = uiCamera.GetComponent<HDAdditionalCameraData>();
            var hdMainCamera = mainCamera.GetComponent<HDAdditionalCameraData>();

            hdUICamera.xrRendering = false;
            uiCamera.stereoTargetEye = StereoTargetEyeMask.None;
            uiCamera.enabled = false;

            hdMainCamera.xrRendering = true;
            mainCamera.stereoTargetEye = StereoTargetEyeMask.Both;
            mainCamera.depth = uiCamera.depth + 1;
            mainCamera.enabled = true;

            hud.RevertHUDFromSpectatorCam();
        }

        public void OnEnterTerminal()
        {
            NonNativeKeyboard.Instance.PresentKeyboard();

            leftControllerRayInteractor.GetComponent<XRInteractorLineVisual>().enabled = true;
            rightControllerRayInteractor.GetComponent<XRInteractorLineVisual>().enabled = true;

            rightController.GetComponent<VRController>().HideDebugLineRenderer();
        }

        public void OnExitTerminal()
        {
            if (NonNativeKeyboard.Instance.isActiveAndEnabled)
                NonNativeKeyboard.Instance.Close();

            leftControllerRayInteractor.GetComponent<XRInteractorLineVisual>().enabled = false;
            rightControllerRayInteractor.GetComponent<XRInteractorLineVisual>().enabled = false;

            rightController.GetComponent<VRController>().ShowDebugLineRenderer();
        }

        public void ResetHeight()
        {
            StartCoroutine(ResetHeightRoutine());
        }

        private IEnumerator ResetHeightRoutine()
        {
            yield return new WaitForSeconds(0.2f);

            var realHeight = mainCamera.transform.localPosition.y * scaleFactor;
            var targetHeight = 2.3f;

            cameraFloorOffset = (targetHeight - realHeight);

            Logger.LogDebug($"Scaling player with real height: {MathF.Round(realHeight*100)/100}cm");
            Logger.Log($"Setting player height scale: {scaleFactor}");
        }

        private Transform Find(string name, bool resetLocalPosition = false)
        {
            var transform = base.transform.Find(name);
            if (transform == null) return null;

            if (resetLocalPosition)
                transform.localPosition = Vector3.zero;

            return transform;
        }

        public static void VibrateController(XRNode hand, float duration, float amplitude)
        {
            UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(hand);

            if (device != null && device.TryGetHapticCapabilities(out HapticCapabilities capabilities) && capabilities.supportsImpulse)
            {
                device.SendHapticImpulse(0, amplitude, duration);
            }
        }
    }

    internal class IKRigFollowVRRig : MonoBehaviour
    {
        [Serializable]
        public class VRMap
        {
            public Transform vrTarget;
            public Transform ikTarget;
            public Vector3 trackingPositionOffset;
            public Vector3 trackingRotationOffset;

            public void Map()
            {
                ikTarget.position = vrTarget.TransformPoint(trackingPositionOffset);
                ikTarget.rotation = vrTarget.rotation * Quaternion.Euler(trackingRotationOffset);
            }
        }

        public Transform head;
        public VRMap leftHand;
        public VRMap rightHand;

        public Vector3 headBodyPositionOffset;

        private void LateUpdate()
        {
            if (head != null)
            {
                transform.position = head.position + headBodyPositionOffset;
            }

            leftHand.Map();
            rightHand.Map();
        }
    }
}
