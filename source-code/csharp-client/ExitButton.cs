using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class exit:MonoBehaviour,
    IPointerClickHandler
{
    private Animator animator;
    private Button button;
    public GameObject main;
    public GameObject menu;

    void Start()
    {
        animator=GetComponent<Animator>();
        button=GetComponent<Button>();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        animator.SetTrigger("isclick");
        menu.SetActive(true);
        main.SetActive(false);
    }
}