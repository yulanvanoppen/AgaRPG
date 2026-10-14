using FishNet.Object;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PositionClippingPlane : NetworkBehaviour
{
    private Material _material;
    public float offset;

    private void Awake()
    {
        _material = GetComponent<MeshRenderer>().material;
    }

    void Update()
    {
        //if (!base.IsOwner) return;
        _material.SetVector("_Point", transform.parent.position - offset * transform.up);
        _material.SetVector("_Normal", -transform.up);
    }
}
