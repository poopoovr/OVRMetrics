using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;

namespace OVRMetrics
{
    public enum HUDLocation
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        Custom
    }

    [BepInPlugin(MyPluginInfo.GUID, MyPluginInfo.Name, MyPluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private ConfigEntry<bool> enabledConfig;
        private ConfigEntry<string> packageNameConfig;
        private ConfigEntry<bool> showPackageNameConfig;
        private ConfigEntry<bool> showBatteryConfig;
        private ConfigEntry<bool> showFPSConfig;
        private ConfigEntry<bool> showPingConfig;
        private ConfigEntry<bool> showPredictionConfig;
        
        private ConfigEntry<int> overrideBatteryConfig;
        private ConfigEntry<int> overridePingConfig;
        private ConfigEntry<int> overridePredictionConfig;

        private ConfigEntry<Vector3> hudPositionConfig;
        private ConfigEntry<float> hudScaleConfig;
        private ConfigEntry<HUDLocation> hudLocationConfig;

        private GameObject canvasObj;
        private Text textComponent;

        private float deltaTime = 0.0f;

        private void Awake()
        {
            enabledConfig = Config.Bind("General", "Enabled", true, "Enable the OVR Metrics HUD");
            
            packageNameConfig = Config.Bind("General", "PackageName", "com.AnotherAxiom.GorillaTag", "Package name to display");
            showPackageNameConfig = Config.Bind("Toggles", "ShowPackageName", true, "Show the package name");
            showBatteryConfig = Config.Bind("Toggles", "ShowBattery", true, "Show battery percentage");
            showFPSConfig = Config.Bind("Toggles", "ShowFPS", true, "Show FPS");
            showPingConfig = Config.Bind("Toggles", "ShowPing", true, "Show ping");
            showPredictionConfig = Config.Bind("Toggles", "ShowPrediction", true, "Show prediction time");

            overrideBatteryConfig = Config.Bind("Overrides", "OverrideBattery", -1, "Set to a value >= 0 to override actual battery");
            overridePingConfig = Config.Bind("Overrides", "OverridePing", -1, "Set to a value >= 0 to override actual ping");
            overridePredictionConfig = Config.Bind("Overrides", "OverridePrediction", -1, "Set to a value >= 0 to override prediction");

            hudPositionConfig = Config.Bind("Positioning", "HUDPosition", new Vector3(0.12f, 0.05f, 0.3f), "Position relative to the camera (only used if HUDLocation is Custom)");
            hudScaleConfig = Config.Bind("Positioning", "HUDScale", 0.0003f, "Scale of the HUD");
            hudLocationConfig = Config.Bind("Positioning", "HUDLocation", HUDLocation.TopRight, "Which corner to place the HUD in");
        }

        private void SetupHUD()
        {
            if (canvasObj != null) return;

            Transform cameraTransform = GorillaTagger.Instance.mainCamera.transform;

            canvasObj = new GameObject("OVRMetricsCanvas");
            canvasObj.transform.SetParent(cameraTransform, false);
            canvasObj.transform.localPosition = hudPositionConfig.Value;
            canvasObj.transform.localRotation = Quaternion.identity;
            canvasObj.transform.localScale = Vector3.one * hudScaleConfig.Value;

            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 32767;

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(canvasObj.transform, false);
            textObj.transform.localPosition = Vector3.zero;
            textObj.transform.localRotation = Quaternion.identity;
            textObj.transform.localScale = Vector3.one;

            textComponent = textObj.AddComponent<Text>();
            
            Font monoFont = null;
            foreach (Font f in Resources.FindObjectsOfTypeAll<Font>())
            {
                string lowerName = f.name.ToLower();
                if (lowerName.Contains("gorilla") || lowerName.Contains("agencb") || lowerName.Contains("consolas") || lowerName.Contains("courier") || lowerName.Contains("mono"))
                {
                    monoFont = f;
                    if (lowerName.Contains("mono") || lowerName.Contains("gorilla")) break;
                }
            }

            if (monoFont == null) monoFont = Font.CreateDynamicFontFromOSFont(new string[] { "Bitblox Monospaced", "Consolas", "Courier New", "Arial" }, 24);
            if (monoFont == null) monoFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            
            textComponent.font = monoFont;
            textComponent.fontSize = 24;
            textComponent.alignment = TextAnchor.LowerLeft;
            textComponent.horizontalOverflow = HorizontalWrapMode.Overflow;
            textComponent.verticalOverflow = VerticalWrapMode.Overflow;
            
            Material alwaysOnTopMat = new Material(Shader.Find("GUI/Text Shader"));
            textComponent.material = alwaysOnTopMat;
            
            Outline outline = textObj.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(1, -1);
            
            UpdateHUDText();
        }

        private void Update()
        {
            if (!enabledConfig.Value)
            {
                if (canvasObj != null && canvasObj.activeSelf) canvasObj.SetActive(false);
                return;
            }
            
            if (canvasObj != null && !canvasObj.activeSelf) canvasObj.SetActive(true);
            if (canvasObj == null && GorillaTagger.Instance != null && GorillaTagger.Instance.mainCamera != null) SetupHUD();

            if (canvasObj != null)
            {
                Vector3 targetPos = hudPositionConfig.Value;
                switch (hudLocationConfig.Value)
                {
                    case HUDLocation.TopLeft: targetPos = new Vector3(-0.08f, 0.1f, 0.3f); break;
                    case HUDLocation.TopRight: targetPos = new Vector3(0.06f, 0.1f, 0.3f); break;
                    case HUDLocation.BottomLeft: targetPos = new Vector3(-0.08f, -0.1f, 0.3f); break;
                    case HUDLocation.BottomRight: targetPos = new Vector3(0.06f, -0.1f, 0.3f); break;
                }
                canvasObj.transform.localPosition = targetPos;
                canvasObj.transform.localScale = Vector3.one * hudScaleConfig.Value;
            }

            deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
            UpdateHUDText();
        }

        private void UpdateHUDText()
        {
            if (textComponent == null) return;

            float fps = 1.0f / deltaTime;
            int displayFPS = Mathf.RoundToInt(fps);

            int battery = overrideBatteryConfig.Value;
            if (battery < 0)
            {
                battery = -1;
                var headDevice = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
                if (headDevice.isValid && headDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.batteryLevel, out float batLevel)) battery = Mathf.RoundToInt(batLevel * 100f);

                if (battery <= 0)
                {
                    try
                    {
                        System.Type ovrType = System.Type.GetType("OVRManager, Assembly-CSharp") ?? System.Type.GetType("OVRManager, Oculus.VR");
                        if (ovrType != null)
                        {
                            var batProp = ovrType.GetProperty("batteryLevel", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                            if (batProp != null)
                            {
                                float ovrBat = (float)batProp.GetValue(null);
                                if (ovrBat > 0f) 
                                {
                                    if (Application.platform == RuntimePlatform.Android || ovrBat < 1.0f) battery = Mathf.RoundToInt(ovrBat * 100f);
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (battery <= 0 && Application.platform == RuntimePlatform.Android)
                {
                    float bat = SystemInfo.batteryLevel;
                    battery = bat >= 0f ? Mathf.RoundToInt(bat * 100f) : 100;
                }
            }

            int ping = overridePingConfig.Value;
            if (ping < 0) ping = PhotonNetwork.InRoom ? PhotonNetwork.GetPing() : 0;

            int pred = overridePredictionConfig.Value;
            if (pred < 0) pred = Mathf.RoundToInt((Time.smoothDeltaTime * 1000f) + 15f);

            string colorWhite = "<color=#ffffff>";
            string colorGreen = "<color=#00ff00>";
            string colorEnd = "</color>";

            string text = "";
            if (showPackageNameConfig.Value) text += $"{colorWhite}{packageNameConfig.Value}{colorEnd}\n";
            if (showBatteryConfig.Value)
            {
                string batStr = battery >= 0 ? battery.ToString() : "N/A";
                text += $"{colorWhite}BAT:  {colorEnd}{colorGreen}{batStr}{colorEnd}\n";
            }
            if (showFPSConfig.Value) text += $"{colorWhite}FPS:  {colorEnd}{colorGreen}{displayFPS}{colorEnd}\n";
            if (showPingConfig.Value) text += $"{colorWhite}MS:   {colorEnd}{colorGreen}{ping}{colorEnd}\n";
            if (showPredictionConfig.Value) text += $"{colorWhite}PRED: {colorEnd}{colorGreen}{pred}{colorEnd}";
            textComponent.text = text;
        }
    }
}
