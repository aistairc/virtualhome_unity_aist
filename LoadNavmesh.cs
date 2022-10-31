using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class LoadNavmesh : MonoBehaviour
{
    [SerializeField] private string assetname;

    private NavMeshDataInstance instance;

    void OnEnable()
    {
        // NavMeshの登録
        var data = Resources.Load<NavMeshData>(assetname);
        instance = NavMesh.AddNavMeshData(data);
    }

    void OnDisable()
    {
        // NavMeshの破棄
        NavMesh.RemoveNavMeshData(instance);
    }
}
