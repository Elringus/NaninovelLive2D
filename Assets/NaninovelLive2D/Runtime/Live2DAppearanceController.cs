using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using System.Collections.Generic;
using System.Linq;
using UnityCommon;
using UnityEngine;

namespace Naninovel
{
    [RequireComponent(typeof(CubismModel))]
    public class Live2DAppearanceController : MonoBehaviour
    {
        [System.Serializable]
        public class ParametersMap : SerializableMap<string, float> { }

        [System.Serializable]
        public class AppearanceData
        {
            [Tooltip("Appearance to bind parameters for.")]
            public string Appearance = string.Empty;
            [Tooltip("List of parameters' name and value to bind.")]
            public ParametersMap Params = default;
        }

        [SerializeField] private List<AppearanceData> appearanceMap = default;
        [SerializeField] private CubismParameterBlendMode blendMode = CubismParameterBlendMode.Override;
        [SerializeField] private string headXAngleParamId = "PARAM_ANGLE_X";

        private CubismModel cubismModel;
        private AppearanceData appearance;
        private float speed;
        private float headXAngle;

        public void SetAppearance (string appearance, float speed = 1f)
        {
            this.appearance = appearanceMap?.FirstOrDefault(a => a.Appearance == appearance);
            this.speed = speed;
        }

        public void SetLookDirection (CharacterLookDirection lookDirection, float speed = 1f)
        {
            switch (lookDirection)
            {
                case CharacterLookDirection.Center:
                    headXAngle = 0f;
                    break;
                case CharacterLookDirection.Left:
                    headXAngle = -30f;
                    break;
                case CharacterLookDirection.Right:
                    headXAngle = 30f;
                    break;
            }
        }

        private void Start ()
        {
            cubismModel = this.FindCubismModel();
        }

        private void LateUpdate ()
        {
            for (int i = 0; i < cubismModel.Parameters.Length; i++)
            {
                var param = cubismModel.Parameters[i];
                var targetValue = default(float);

                if (param.Id == headXAngleParamId) targetValue = headXAngle;
                else targetValue = appearance is null || !appearance.Params.ContainsKey(param.Id) ? param.DefaultValue : appearance.Params[param.Id];
                targetValue = Mathf.Clamp(targetValue, param.MinimumValue, param.MaximumValue);

                if (Mathf.Approximately(param.Value, targetValue)) continue;

                var value = Mathf.Lerp(param.Value, targetValue, Time.deltaTime * speed * 5f);

                param.BlendToValue(blendMode, value);
            }
        }
    }
}
