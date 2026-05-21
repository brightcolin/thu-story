using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class play:MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
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

    public void OnPointerEnter(PointerEventData eventData)
    {
            animator.SetTrigger("isenter");
    }

    public void OnPointerExit(PointerEventData eventData)
    {
            animator.SetTrigger("isexit");
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        animator.SetTrigger("isclick");
        menu.SetActive(false);
        main.SetActive(true);
    }
}