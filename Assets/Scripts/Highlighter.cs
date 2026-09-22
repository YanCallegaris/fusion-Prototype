using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Highlighter : MonoBehaviour
{
    private Highlightable current;

    public void UpdateHighlightable(Vector3 origin, Vector3 direction, Player player)
    {
        Highlightable highlightable = null;
        if (Physics.Raycast(origin, direction, out RaycastHit hitInfo, player.AbillityRange))
        {
            if (player.GrappleCDFactor == 0f)
                highlightable = hitInfo.collider.GetComponent<Highlightable>();

            switch (player.SelectedAbility)
            {
                case Abilitymode.BreakBlock:
                    if (player.BreakCDFactor == 0f)
                        highlightable = hitInfo.collider.GetComponent<Highlightable>();
                    break;
                case Abilitymode.Cage:
                    if (player.CageCDFactor == 0f)
                    {
                        if (hitInfo.rigidbody != null) // Hit a player
                            highlightable = hitInfo.rigidbody.GetComponent<Highlightable>();
                    }
                    break;
                case Abilitymode.Shove:
                    if (player.ShoveCDFactor == 0f)
                    {
                        if (hitInfo.rigidbody != null) //Hit a player
                            highlightable = hitInfo.rigidbody.GetComponent<Highlightable>();
                    }
                    break;
                default:
                    break;
            }
        }
        if(highlightable != current)
        {
            if (current != null)
                current.Highlight(false);

            current = highlightable;
            if (current != null)
                current.Highlight(true);
        }
    }
}
