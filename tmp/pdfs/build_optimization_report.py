from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    HRFlowable,
    KeepTogether,
    PageBreak,
    Paragraph,
    SimpleDocTemplate,
    Spacer,
    Table,
    TableStyle,
)


ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "output" / "pdf" / "zharkoe_leto_44_go_optimization_report.pdf"
OUT.parent.mkdir(parents=True, exist_ok=True)

pdfmetrics.registerFont(TTFont("Arial", r"C:\Windows\Fonts\arial.ttf"))
pdfmetrics.registerFont(TTFont("ArialBold", r"C:\Windows\Fonts\arialbd.ttf"))
pdfmetrics.registerFontFamily("Arial", normal="Arial", bold="ArialBold")

NAVY = colors.HexColor("#172738")
BLUE = colors.HexColor("#23648b")
PALE = colors.HexColor("#edf4f7")
LIGHT = colors.HexColor("#f5f7f9")
GREY = colors.HexColor("#536474")
RULE = colors.HexColor("#d5dfe5")
AMBER = colors.HexColor("#a76711")

styles = {
    "title": ParagraphStyle(
        "Title", fontName="ArialBold", fontSize=23, leading=28, textColor=NAVY,
        spaceAfter=9, alignment=TA_LEFT,
    ),
    "subtitle": ParagraphStyle(
        "Subtitle", fontName="Arial", fontSize=10, leading=14, textColor=GREY,
        spaceAfter=17,
    ),
    "h1": ParagraphStyle(
        "H1", fontName="ArialBold", fontSize=13, leading=16, textColor=NAVY,
        spaceBefore=16, spaceAfter=8,
    ),
    "h2": ParagraphStyle(
        "H2", fontName="ArialBold", fontSize=10.5, leading=14, textColor=BLUE,
        spaceBefore=11, spaceAfter=5,
    ),
    "body": ParagraphStyle(
        "Body", fontName="Arial", fontSize=9.3, leading=13.4, textColor=NAVY,
        spaceAfter=7,
    ),
    "small": ParagraphStyle(
        "Small", fontName="Arial", fontSize=8.2, leading=11, textColor=GREY,
        spaceAfter=5,
    ),
    "tablehead": ParagraphStyle(
        "TableHead", fontName="ArialBold", fontSize=8.4, leading=11,
        textColor=colors.white,
    ),
    "cell": ParagraphStyle(
        "Cell", fontName="Arial", fontSize=8.5, leading=11.8, textColor=NAVY,
    ),
    "cellbold": ParagraphStyle(
        "CellBold", fontName="ArialBold", fontSize=8.5, leading=11.8,
        textColor=NAVY,
    ),
}


def p(text, style="body"):
    return Paragraph(text, styles[style])


def bullet(text):
    return p("<font color='#23648b'><b>•</b></font>  " + text)


def section(title):
    return p(title, "h1")


def footer(canvas, doc):
    canvas.saveState()
    width, _ = A4
    canvas.setStrokeColor(RULE)
    canvas.line(19 * mm, 18 * mm, width - 19 * mm, 18 * mm)
    canvas.setFont("Arial", 8)
    canvas.setFillColor(GREY)
    canvas.drawString(19 * mm, 13 * mm, "Жаркое лето 44-го  |  Технический отчёт")
    canvas.drawRightString(width - 19 * mm, 13 * mm, str(doc.page))
    canvas.restoreState()


doc = SimpleDocTemplate(
    str(OUT), pagesize=A4,
    leftMargin=19 * mm, rightMargin=19 * mm,
    topMargin=19 * mm, bottomMargin=23 * mm,
    title="Жаркое лето 44-го — отчёт об оптимизации и освещении",
    author="Codex",
)

story = []
story.append(p("Аудит производительности<br/>и освещения", "title"))
story.append(p("Жаркое лето 44-го  •  Unity 2022.3.62f3  •  14 октября 2026", "subtitle"))
story.append(HRFlowable(width="100%", thickness=1, color=RULE, spaceAfter=12))
story.append(p(
    "Проверены горячие участки игрового кода и сохранённая сцена Main menu. "
    "Ниже разделены уже исправленные источники лишней нагрузки и отдельная "
    "проблема запекания света, которая пока остаётся в сцене."
))

summary = Table(
    [[p("3", "cellbold"), p("5", "cellbold"), p("0", "cellbold")],
     [p("группы проблем с CPU/GC", "cell"), p("изменённых скриптов", "cell"),
      p("изменений графических настроек", "cell")]],
    colWidths=[51 * mm, 51 * mm, 51 * mm],
)
summary.setStyle(TableStyle([
    ("BACKGROUND", (0, 0), (-1, -1), PALE),
    ("BOX", (0, 0), (-1, -1), 0.6, RULE),
    ("INNERGRID", (0, 0), (-1, -1), 0.5, RULE),
    ("TOPPADDING", (0, 0), (-1, 0), 9),
    ("BOTTOMPADDING", (0, -1), (-1, -1), 9),
    ("LEFTPADDING", (0, 0), (-1, -1), 10),
]))
story.extend([Spacer(1, 4 * mm), summary, section("Что было не так")])

rows = [
    [p("Участок", "tablehead"), p("Наблюдение и последствие", "tablehead"),
     p("Состояние", "tablehead")],
    [p("Подбор предметов", "cellbold"),
     p("Каждый кадр выполнялись SphereCastAll и OverlapSphere; при проверке видимости добавлялся RaycastAll. Каждый вызов создавал массив результатов, повышая нагрузку на сборщик мусора.", "cell"),
     p("Исправлено", "cell")],
    [p("Обход препятствий", "cellbold"),
     p("В резервном режиме движения враг мог многократно вызывать SphereCastAll при переборе направлений и RaycastAll при поиске земли. При нескольких врагах число временных массивов росло.", "cell"),
     p("Исправлено", "cell")],
    [p("Диалоги", "cellbold"),
     p("Триггеры проверяли прямую видимость игрока и за пределами радиуса взаимодействия. Сама проверка использовала RaycastAll и выделяла массив.", "cell"),
     p("Исправлено", "cell")],
    [p("Солдат в Main menu", "cellbold"),
     p("Red Army soldier(LP) состоит из SkinnedMeshRenderer, но объект и дочерние части помечены всеми флагами Static. В сохранённой сцене нет LightProbeGroup и не назначен LightingDataAsset. Это согласуется с почти чёрной моделью после запекания.", "cell"),
     p("Требует исправления", "cell")],
]
table = Table(rows, colWidths=[31 * mm, 98 * mm, 24 * mm], repeatRows=1)
table.setStyle(TableStyle([
    ("BACKGROUND", (0, 0), (-1, 0), NAVY),
    ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, LIGHT]),
    ("VALIGN", (0, 0), (-1, -1), "TOP"),
    ("LINEBELOW", (0, 0), (-1, 0), 0.8, NAVY),
    ("LINEBELOW", (0, 1), (-1, -2), 0.4, RULE),
    ("LEFTPADDING", (0, 0), (-1, -1), 7),
    ("RIGHTPADDING", (0, 0), (-1, -1), 7),
    ("TOPPADDING", (0, 0), (-1, -1), 7),
    ("BOTTOMPADDING", (0, 0), (-1, -1), 7),
]))
story.append(table)
story.append(Spacer(1, 4 * mm))
story.append(p(
    "<b>Важно:</b> по исходному коду можно подтвердить лишние выделения памяти, "
    "но величина прироста FPS без профилирования запущенной игры не измерена.", "small"
))

story.append(PageBreak())
story.append(p("Что изменено и как проверено", "title"))
story.append(p("Исправления касаются только логики CPU и выделения памяти.", "subtitle"))
story.append(HRFlowable(width="100%", thickness=1, color=RULE, spaceAfter=8))
story.append(section("Внесённые изменения"))
story.append(bullet(
    "<b>InventorySystem:</b> переиспользуемые буферы для поиска предметов и проверки "
    "перекрытия. Если буфер заполнен, выполняется прежний запрос без ограничения числа попаданий."
))
story.append(bullet(
    "<b>EnemyMotor:</b> такой же подход для проверок пути и земли в резервном режиме "
    "движения без NavMesh."
))
story.append(bullet(
    "<b>DialogueManager, DialogueTrigger, NpcDialogueSequence:</b> буфер для лучевых "
    "проверок и ранний пропуск проверки видимости, когда игрок вне радиуса диалога."
))
story.append(p(
    "Порядок обработки попаданий при проверках видимости сохранён сортировкой по расстоянию. "
    "Материалы, освещение, камеры, качество рендера и файлы сцен в рамках этой оптимизации не менялись."
))

story.append(section("Проверка"))
story.append(bullet(
    "Все пять изменённых скриптов собраны Unity 2022.3.62f3 в отдельной копии проекта: "
    "<b>Tundra build success</b>, процесс завершился с кодом 0."
))
story.append(bullet(
    "Проверка diff не обнаружила ошибок форматирования в изменённых скриптах. "
    "Запуск игры с замером Profiler CPU/GC и сравнением кадров до/после ещё не проводился."
))

story.append(section("Что ещё нужно сделать с солдатом"))
story.append(p(
    "Для Red Army soldier(LP) убрать флаги Static у корня и дочерних частей с "
    "SkinnedMeshRenderer, разместить пробы освещения вокруг сидящего персонажа, "
    "назначить корректную точку выборки света и заново запечь Main menu. "
    "После запекания проверить модель в Game View. Это отдельная правка визуального "
    "результата; в описанную CPU-оптимизацию она не входила."
))

story.append(section("Проверяемые источники"))
story.append(p(
    "Код проекта: Assets/All Project/Scripts/Inventory/InventorySystem.cs; "
    "AI/EnemyMotor.cs; Dialogs/DialogueManager.cs; Dialogs/DialogueTrigger.cs; "
    "Dialogs/NpcDialogueSequence.cs. Сцена: Assets/All Project/Scenes/Main menu.unity.", "small"
))
story.append(p(
    "Unity 2022.3 Manual: "
    "<link href='https://docs.unity3d.com/2022.3/Documentation/Manual/class-SkinnedMeshRenderer.html' color='#23648b'>"
    "Skinned Mesh Renderer</link>. Unity 2022.3 Scripting API: "
    "<link href='https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Physics.RaycastNonAlloc.html' color='#23648b'>"
    "Physics.RaycastNonAlloc</link>.", "small"
))

doc.build(story, onFirstPage=footer, onLaterPages=footer)
print(OUT)
