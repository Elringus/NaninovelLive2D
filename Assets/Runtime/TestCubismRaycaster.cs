using System.Linq;
using Live2D.Cubism.Framework.Raycasting;
using Naninovel;
using UnityEngine;

[RequireComponent(typeof(CubismRaycaster))]
public class TestCubismRaycaster : MonoBehaviour
{
    private readonly CubismRaycastHit[] raycastHits = new CubismRaycastHit[4];
    private CubismRaycaster cubismRaycaster;
    private ICameraManager cameraManager;
    
    private void Awake ()
    {
        cameraManager = Engine.GetService<ICameraManager>();
        cubismRaycaster = GetComponent<CubismRaycaster>();
    }

    private void Update()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        for (int i = 0; i < raycastHits.Length; i++)
            raycastHits[i] = default;
        
        var ray = cameraManager.Camera.ScreenPointToRay(Input.mousePosition);
        var count = cubismRaycaster.Raycast(ray, raycastHits);
        if (count == 0) return;

        var resultText = string.Join(",", raycastHits
            .Where(h => h.Drawable)
            .Select(h => h.Drawable.name));
        Debug.Log(resultText);
    }
}
