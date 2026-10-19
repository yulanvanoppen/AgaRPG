using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using FishNet;
using FishNet.Managing.Scened;
using FishNet.Transporting.UTP;
using Unity.Services.CloudSave;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Authentication.PlayerAccounts.Samples;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.UI;
using System.Threading;

public class CloudSaveManager : MonoBehaviour
{
    const string _tagStateKey = "TAG_STATE";
    const string _sizeStateKey = "SIZE_STATE";
    const string _colorStateKey = "COLOR_STATE";

    [HideInInspector] public string playerId = string.Empty;
    [HideInInspector] public string playerTag;
    [HideInInspector] public float playerSize = float.NaN;
    [HideInInspector] public Color playerColor;

    public Text playerText;
    public InputField tagField;

    [SerializeField] private AuthenticationManager _authenticationManager;


    protected virtual void Awake()
    {
        GameObject[] objs = GameObject.FindGameObjectsWithTag("CloudSaveManager");
        if (objs.Length > 1)
        {
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(this.gameObject);
        playerColor = Color.white;
    }


    public async void EditTag()
    {
        await Authentication.SetPlayerTag(tagField.text);
        playerTag = Authentication.playerTag;
        tagField.text = playerTag;
        Debug.Log(playerTag);
    }


    public void UpdateText()
    {
        playerText.text = $"ID: {Authentication.playerId} (SIZE: {playerSize * 100f:0}%)";
        tagField.text = Authentication.playerTag;
    }


    public async Task FetchPlayerData()
    {
        try
        {
            // values come back as Item objects; read them as strings with GetAsString()
            var cloudSaveData = await CloudSaveService.Instance.Data.Player.LoadAllAsync();

            if (cloudSaveData.ContainsKey(_sizeStateKey))
                playerSize = float.Parse(cloudSaveData[_sizeStateKey].Value.GetAsString(),
                                         CultureInfo.InvariantCulture);
            else
            {
                playerSize = 1f;
                await UpdateSize(playerSize);
            }

            if (cloudSaveData.ContainsKey(_colorStateKey))
                ColorUtility.TryParseHtmlString('#' + cloudSaveData[_colorStateKey].Value.GetAsString(),
                                                out playerColor);
            else
            {
                playerColor = Color.white;
                await UpdateColor(playerColor);
            }

            playerTag = Authentication.playerTag;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    public virtual async Task UpdateSize(float value)
    {
        playerSize = value;
        try
        {
            var dataToSave = new Dictionary<string, object> { { _sizeStateKey, playerSize } };

            await CloudSaveService.Instance.Data.Player.SaveAsync(dataToSave);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    public virtual async Task UpdateColor(Color value)
    {
        playerColor = value;
        try
        {
            var dataToSave = new Dictionary<string, object>
            {
                { _colorStateKey, ColorUtility.ToHtmlStringRGBA(value) }
            };

            await CloudSaveService.Instance.Data.Player.SaveAsync(dataToSave);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }
}
