using System;
using System.Numerics;
using Infohazard.Core;
using TMPro;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace Infohazard.Demos {
    public class QuarticGrapher : MonoBehaviour {
        [SerializeField] private LineRenderer _line;
        [SerializeField] private float _scale = 1;
        [SerializeField] private int _points = 1000;
        [SerializeField] private float _domainMin = -5;
        [SerializeField] private float _domainMax = 5;

        [SerializeField] private TMP_Text _rootsText;
        [SerializeField] private TMP_InputField _c1, _c2, _c3, _c4, _c5;

        private void Start() {
            UpdateGraph();
        }

        private float GetValue(TMP_InputField field) {
            return float.TryParse(field.text, out float value) ? value : 0;
        }

        public void UpdateGraph() {
            float f1 = GetValue(_c1);
            float f2 = GetValue(_c2);
            float f3 = GetValue(_c3);
            float f4 = GetValue(_c4);
            float f5 = GetValue(_c5);

            _line.positionCount = _points;
            for (int i = 0; i < _points; i++) {
                float u = (i / (_points - 1.0f));
                float x = Mathf.Lerp(_domainMin, _domainMax, u);

                float y = f1 * x * x * x * x +
                          f2 * x * x * x +
                          f3 * x * x +
                          f4 * x +
                          f5;

                _line.SetPosition(i, new Vector3(x, y, 0) * _scale);
            }

            var roots =MathUtility.SolveQuartic(f1, f2, f3, f4, f5);
            _rootsText.text = "Roots: ";
            CheckRoot(roots.r1);
            CheckRoot(roots.r2);
            CheckRoot(roots.r3);
            CheckRoot(roots.r4);
        }

        private void CheckRoot(Complex number) {
            if (Math.Abs(number.Imaginary) < 0.0000001) {
                _rootsText.text += $"\n{number.Real}";
            }
        }

        public void SetScale(float value) {
            _scale = value;
            UpdateGraph();
        }
    }
}