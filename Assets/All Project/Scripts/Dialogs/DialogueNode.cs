using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class DialogueNode
{
    [Header("Идентификатор узла")]
    public string nodeID;
    public string nextNodeID;

    [Header("Персонаж и текст")]
    public string speakerName;
    public Sprite speakerPortrait;
    [Tooltip("Цвет имени говорящего. Прозрачный (по умолчанию) = авто-палитра по имени.")]
    public Color speakerColor = new Color(0f, 0f, 0f, 0f);
    [TextArea(3, 10)]
    public string dialogueText;
    public AudioClip voiceClip;
    [Tooltip("Произносить реплику с немецким акцентом: слова латиницей станут " +
             "русской фонетикой («bitte» → «битте», «Wasser» → «вассер»), " +
             "в начало реплики иногда добавится запинка. Для реплик немца.")]
    public bool germanAccent = false;

    [Header("Скорость и паузы")]
    public float textSpeed = 0.05f;
    public float autoAdvanceDelay = 0f;

    [Header("Команды при входе")]
    public List<DialogueCommand> onEnterCommands = new List<DialogueCommand>();

    [Header("Ответы (ветвление)")]
    public List<DialogueChoice> choices = new List<DialogueChoice>();

    [Header("События Unity")]
    public UnityEvent onNodeEnter;
    public UnityEvent onNodeExit;
}