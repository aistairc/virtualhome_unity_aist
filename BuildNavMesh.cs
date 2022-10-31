using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class BuildNavMesh : MonoBehaviour
{
    #if UNITY_EDITOR

    [ContextMenu("CreateNavmesh")]
    void Create()
    {
        var sources = new List<NavMeshBuildSource>();
        var markups = new List<NavMeshBuildMarkup>();
        var settings = NavMesh.GetSettingsByID(0);
        var bounds = new Bounds(Vector3.zero, 10.0f * Vector3.one);

        // Collect objects to build Navmesh
        UnityEditor.AI.NavMeshBuilder.CollectSourcesInStage(
            root: transform,                                // nullの場合Scene全体が含まれます。transformを指定した場合はルートとその子のみを考慮します。
            includedLayerMask: ~0,                          // クェリに含めるレイヤー
            geometry: NavMeshCollectGeometry.RenderMeshes,  // 収集するジオメトリを選択します。レンダラーかコライダー
            defaultArea: 0,                                 // 割り当てるエリアタイプ
            markups: markups,                               // 収集方法についてのマークアップリスト(含めないエリアとか色々）
            stageProxy: gameObject.scene,                   // 所属するシーン
            results: sources);                              // ベイクに使用するジオメトリのリスト（out)

        // Build NavMesh
        var navmesh = NavMeshBuilder.BuildNavMeshData(
            buildSettings: settings,                    // ベイク処理の設定
            sources: sources,                           // ベイクに使用するジオメトリのリスト
            localBounds: bounds,                        // NavMeshを構築する範囲
            position: transform.position,               // NavMeshの原点
            rotation: transform.rotation);              // NavMeshの向き


        var exportPath = $"Assets/Resources/{gameObject.name}.asset";
        UnityEditor.AssetDatabase.CreateAsset(navmesh, exportPath);
        UnityEditor.AssetDatabase.Refresh();
    }

#endif
}
