using UnityEditor;
using UnityEngine;
using TenteEEEE.Kazamachi;

namespace TenteEEEE.Kazamachi.Editor
{
    public static class KazamachiMenu
    {
        [MenuItem("GameObject/Kazamachi", false, 20)]
        private static void Create(MenuCommand command)
        {
            var gameObject = new GameObject("Kazamachi");
            GameObjectUtility.SetParentAndAlign(gameObject, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(gameObject, "Create Kazamachi");
            gameObject.AddComponent<KazamachiWind>();
            Selection.activeGameObject = gameObject;
        }
    }
}
