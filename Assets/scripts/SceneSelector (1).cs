using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using System.Collections.Generic;

public class SceneSelector : MonoBehaviour
{
    public string[] sceneNames = new string[]
    {
        "Tacticast_CLIP1",
        "Tacticast_CLIP1 - baseline",
        "Tacticast_CLIP2",
        "Tacticast_CLIP2 - baseline"
    };

    private static SceneSelector _instance;
    private InputDevice _rightController;
    private InputDevice _leftController;

    private bool _aPressedLast;
    private bool _bPressedLast;
    private bool _xPressedLast;
    private bool _yPressedLast;

    private void Awake()
    {
        if (_instance != null) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        TryGetDevices();

        // Right controller: A=scene1, B=scene2
        _rightController.TryGetFeatureValue(CommonUsages.primaryButton, out bool aPressed);
        _rightController.TryGetFeatureValue(CommonUsages.secondaryButton, out bool bPressed);

        // Left controller: X=scene3, Y=scene4
        _leftController.TryGetFeatureValue(CommonUsages.primaryButton, out bool xPressed);
        _leftController.TryGetFeatureValue(CommonUsages.secondaryButton, out bool yPressed);

        if (aPressed && !_aPressedLast) LoadScene(0);
        if (bPressed && !_bPressedLast) LoadScene(1);
        if (xPressed && !_xPressedLast) LoadScene(2);
        if (yPressed && !_yPressedLast) LoadScene(3);

        _aPressedLast = aPressed;
        _bPressedLast = bPressed;
        _xPressedLast = xPressed;
        _yPressedLast = yPressed;
    }

    private void TryGetDevices()
    {
        if (!_rightController.isValid)
        {
            var devices = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, devices);
            if (devices.Count > 0) _rightController = devices[0];
        }
        if (!_leftController.isValid)
        {
            var devices = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, devices);
            if (devices.Count > 0) _leftController = devices[0];
        }
    }

    private void LoadScene(int index)
    {
        if (index < sceneNames.Length)
            SceneManager.LoadScene(sceneNames[index]);
    }
}