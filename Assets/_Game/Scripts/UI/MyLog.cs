using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Serialization;

namespace AgaRPG.UI
{
    /// <summary>
    /// On-screen log viewer for builds (toggle with the Show/Hide button). Only active in the
    /// editor and development builds.
    /// </summary>
    public class MyLog : MonoBehaviour
    {
        [FormerlySerializedAs("maxLines")]
        [SerializeField] private int _maxLines = 30;

        private readonly Queue<string> _lines = new Queue<string>();
        private readonly StringBuilder _builder = new StringBuilder();
        private string _text = string.Empty;
        private bool _hidden = true;

        private void Awake()
        {
            if (!Debug.isDebugBuild)
            {
                enabled = false;
                return;
            }

            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable() => Application.logMessageReceived += HandleLog;
        private void OnDisable() => Application.logMessageReceived -= HandleLog;

        private void HandleLog(string message, string stackTrace, LogType type)
        {
            _lines.Enqueue($"\n [{type}] : {message}");
            if (type == LogType.Exception) _lines.Enqueue("\n" + stackTrace);
            while (_lines.Count > _maxLines) _lines.Dequeue();

            _builder.Clear();
            foreach (string line in _lines) _builder.Append(line);
            _text = _builder.ToString();
        }

        private void OnGUI()
        {
            var buttonRect = new Rect(Screen.width - 100, 10, 80, 20);
            if (!_hidden) GUI.TextArea(new Rect(0, 0, Screen.width / 3f, Screen.height), _text);
            if (GUI.Button(buttonRect, _hidden ? "Show" : "Hide")) _hidden = !_hidden;
        }
    }
}
