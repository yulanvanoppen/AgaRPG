using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SilenceOnServer : MonoBehaviour
{
    void Start() => AudioListener.volume = Application.isBatchMode ? 0f : 1f;
}
