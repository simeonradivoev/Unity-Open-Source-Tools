using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Wizards
{
	public class ReplaceWithPrefab : ScriptableWizard
	{
		public GameObject prefab;
		public int startCountAt;
		[Tooltip("The name of the new objects. use {0} to indicate the number or {1} to indicate the prefab name")] public string namePattern;
		[Tooltip("Copy all Transform information like position, rotation and scale.")] public bool copyTransform = true;
		public GameObject[] objectsToReplace;

		[MenuItem("GameObject/Replace With Prefab")]
		public static void Replace()
		{
			Replace(PrefabBrowser.SelectedPrefab);
		}

		public static void Replace(GameObject prefab)
		{
			ReplaceWithPrefab wizard = DisplayWizard<ReplaceWithPrefab>("Replace With Prefab", "Replace!");
			wizard.objectsToReplace = Selection.GetFiltered(typeof (GameObject), SelectionMode.ExcludePrefab).Cast<GameObject>().ToArray();
			wizard.prefab = prefab;
		}

		private void OnWizardCreate()
		{
			if (prefab != null)
			{
				for (int i = 0; i < objectsToReplace.Length; i++)
				{
					Vector3 position = objectsToReplace[i].transform.position;
					Quaternion rotation = objectsToReplace[i].transform.rotation;
					Vector3 scale = objectsToReplace[i].transform.localScale;

					GameObject newObject = PrefabUtility.ConnectGameObjectToPrefab(objectsToReplace[i], prefab);
					if (!string.IsNullOrEmpty(namePattern))
					{
						newObject.name = string.Format(namePattern, startCountAt + i, prefab.name);
					}
					if (copyTransform)
					{
						newObject.transform.position = position;
						newObject.transform.rotation = rotation;
						newObject.transform.localScale = scale;
					}
				}
			}
		}
	}
}