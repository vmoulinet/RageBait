using UnityEngine;
using UnityEditor;

// Outil d'edition : aplatit le scale + la rotation d'un transform parent (ex: BrokenFrameRoot)
// tout en preservant la pose MONDE de chacun de ses enfants.
//
// Pourquoi : les Rigidbody dynamiques sous un parent au scale != 1 (et/ou tourne) sont mal geres
// par PhysX, surtout avec l'interpolation activee -> les fragments "explosent" vers un point degenere.
// On remet donc le parent a scale 1 / rotation identite, et on reapplique la pose monde des enfants
// pour que rien ne bouge visuellement, mais que les enfants vivent desormais dans un repere propre.
//
// Usage : selectionner le transform parent a aplatir (BrokenFrameRoot) dans la Hierarchy
// (en mode edition du prefab de preference), puis Tools > Debris > Flatten Selected Transform Scale.
public static class FlattenChildScale
{
	[MenuItem("Tools/Debris/Flatten Selected Transform Scale")]
	static void FlattenSelected()
	{
		Transform parent = Selection.activeTransform;
		if (parent == null)
		{
			EditorUtility.DisplayDialog("Flatten Scale", "Selectionne d'abord le transform parent a aplatir (ex: BrokenFrameRoot).", "OK");
			return;
		}

		int child_count = parent.childCount;
		if (child_count == 0)
		{
			EditorUtility.DisplayDialog("Flatten Scale", "Le transform selectionne n'a pas d'enfants.", "OK");
			return;
		}

		// On enregistre l'etat pour Undo (parent + tous les enfants).
		Undo.RegisterFullObjectHierarchyUndo(parent.gameObject, "Flatten Transform Scale");

		// 1) Memoriser la pose MONDE de chaque enfant direct.
		Transform[] children = new Transform[child_count];
		Vector3[] world_positions = new Vector3[child_count];
		Quaternion[] world_rotations = new Quaternion[child_count];
		Vector3[] world_lossy_scales = new Vector3[child_count];

		for (int i = 0; i < child_count; i++)
		{
			Transform child = parent.GetChild(i);
			children[i] = child;
			world_positions[i] = child.position;
			world_rotations[i] = child.rotation;
			world_lossy_scales[i] = child.lossyScale;
		}

		// 2) Aplatir le parent : rotation identite + scale 1, en gardant sa position monde.
		parent.rotation = Quaternion.identity;
		parent.localScale = Vector3.one;

		// 3) Reappliquer la pose monde sur chaque enfant. Comme le parent est maintenant
		//    en rotation identite et scale 1, la localScale = lossyScale voulue.
		for (int i = 0; i < child_count; i++)
		{
			Transform child = children[i];
			child.position = world_positions[i];
			child.rotation = world_rotations[i];

			Vector3 parent_lossy = parent.lossyScale;
			child.localScale = new Vector3(
				SafeDivide(world_lossy_scales[i].x, parent_lossy.x),
				SafeDivide(world_lossy_scales[i].y, parent_lossy.y),
				SafeDivide(world_lossy_scales[i].z, parent_lossy.z)
			);
		}

		EditorUtility.SetDirty(parent.gameObject);
		Debug.Log("[FlattenChildScale] '" + parent.name + "' aplati a scale 1 / rotation identite. " + child_count + " enfants repositionnes en conservant leur pose monde.");
	}

	static float SafeDivide(float a, float b)
	{
		return Mathf.Abs(b) < 1e-6f ? a : a / b;
	}
}
