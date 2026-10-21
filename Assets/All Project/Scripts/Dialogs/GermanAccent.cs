using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Немецкий акцент для реплик: переводит немецкие слова в русскую фонетику
/// («bitte» → «битте», «Wasser» → «вассер») и добавляет заминки на «немецкий» манер.
/// Работает по словарю + общим правилам транслитерации, поэтому подходит и для
/// неизвестных латинских слов.
/// </summary>
public static class GermanAccent
{
    /// <summary>Словарь: немецкое слово → русская фонетическая запись.</summary>
    static readonly Dictionary<string, string> Words = BuildWords();

    /// <summary>
    /// Словарь собираем через индексатор: опечатка с повтором слова не должна
    /// ронять игру в статическом конструкторе (Exception на дубликате ключа).
    /// </summary>
    static Dictionary<string, string> BuildWords()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string key, string value) => d[key] = value;

        // Приветствия и базовые слова
        Add("ja", "я");
        Add("nein", "нет");
        Add("bitte", "битте");
        Add("danke", "данке");
        Add("dankeschon", "данкешон");
        Add("herr", "гер");
        Add("herren", "геррен");
        Add("frau", "фрау");
        Add("junger", "юнгер");
        Add("junge", "юнге");
        Add("soldat", "зольдат");
        Add("soldaten", "зольдатэн");
        Add("offizier", "офицер");
        Add("hauptmann", "хауптман");
        Add("leutnant", "лойтнант");
        Add("ich", "их");
        Add("ihr", "ир");
        Add("mich", "мих");
        Add("mir", "мир");
        Add("mein", "мойн");
        Add("meine", "мойнэ");
        Add("dein", "дайн");
        Add("dir", "дир");
        Add("dich", "дих");
        Add("uns", "унс");
        Add("nicht", "нихт");
        Add("nichts", "нихтс");
        Add("auch", "аух");
        Add("nur", "нур");
        Add("noch", "нох");
        Add("schon", "шон");
        Add("sehr", "зер");
        Add("wirklich", "вирклих");
        Add("hier", "хиер");
        Add("dort", "дорт");
        Add("da", "да");
        Add("weg", "вег");
        Add("weiter", "вайтер");
        Add("gut", "гут");
        Add("gute", "гуте");
        Add("besser", "бессер");
        Add("schlecht", "шлехт");
        Add("haben", "хабен");
        Add("habe", "хабе");
        Add("hat", "хат");
        Add("habt", "хабт");
        Add("sein", "зайн");
        Add("seine", "зайнэ");
        Add("bin", "бин");
        Add("sind", "зинд");
        Add("ist", "ист");
        Add("kann", "канн");
        Add("können", "кёнен");
        Add("wird", "вирд");
        Add("werden", "верден");
        Add("kommen", "коммен");
        Add("gehen", "геен");
        Add("sehen", "зеен");
        Add("wissen", "виссен");
        Add("so", "зо");
        Add("was", "вас");
        Add("warum", "варум");
        Add("wann", "ван");
        Add("wo", "во");
        Add("wer", "вер");
        Add("welche", "велхе");
        Add("dieser", "дизер");
        Add("jener", "йенер");

        // Еда и быт (главное для сцены)
        Add("brot", "брот");
        Add("wasser", "вассер");
        Add("essen", "ессен");
        Add("ess", "есс");
        Add("trinken", "тринкен");
        Add("trink", "тринк");
        Add("durstig", "дурстиг");
        Add("hungrig", "хунгриг");
        Add("hunger", "хунгер");
        Add("kalt", "кальт");
        Add("müde", "мюдэ");
        Add("warm", "варм");
        Add("tabak", "табак");
        Add("zigarre", "цигаррэ");
        Add("zigarren", "цигаррэн");
        Add("rauch", "рах");
        Add("feuer", "фойер");
        Add("licht", "лихт");
        Add("streichholz", "штрихольц");
        Add("streichholzer", "штрихольцэр");
        Add("zunder", "цундэр");
        Add("tabakware", "табакваре");
        Add("dose", "дозэ");
        Add("salz", "зальц");
        Add("fleisch", "фляйш");
        Add("suppe", "зуппэ");
        Add("kase", "казэ");
        Add("kaffee", "кафэ");
        Add("schnaps", "шнапс");

        // Дом, улица, лес
        Add("dorf", "дорф");
        Add("straße", "штрассе");
        Add("strasse", "штрассе");
        Add("haus", "хаус");
        Add("tür", "тюр");
        Add("tuer", "тюр");
        Add("fenster", "фенстер");
        Add("zimmer", "циммер");
        Add("wald", "вальд");
        Add("brücke", "брюкке");
        Add("bruecke", "брюкке");
        Add("heide", "хайдэ");
        Add("feld", "фельд");
        Add("boden", "бодэн");
        Add("schnee", "шнеэ");
        Add("nacht", "нахт");
        Add("morgen", "моргэн");
        Add("abend", "абэнд");
        Add("tag", "таг");

        // Война, оружие, патруль
        Add("waffe", "ваффэ");
        Add("waffen", "ваффэн");
        Add("gewehr", "гэвер");
        Add("granate", "гранатэ");
        Add("patronen", "патронэн");
        Add("munition", "муницион");
        Add("posten", "постэн");
        Add("wache", "вахэ");
        Add("patrouille", "патрульэ");
        Add("kommando", "командо");
        Add("schiessen", "шиссэн");
        Add("schießen", "шиссэн");
        Add("nicht schießen", "нихт шиссэн");
        Add("hande hoch", "хэнде хох");
        Add("hands up", "хэнс ап");
        Add("kapitulation", "капитуляцион");
        Add("lazarett", "лазаретт");
        Add("verwundet", "фервундет");
        Add("tot", "тот");
        Add("kamerad", "камерад");
        Add("kameraden", "камерадэн");
        // Побор, налёт на деревню (требования, приказы, грабёж)
        Add("auf", "ауп");
        Add("und", "унд");
        Add("schnell", "шнэль");
        Add("mann", "ман");
        Add("sechs", "зекс");
        Add("wie", "ви");
        Add("du", "ду");
        Add("willst", "вильст");
        Add("kämpfen", "кэмпфэн");
        Add("kampfen", "кэмпфэн");
        Add("wiedersehen", "видерзэен");

        Add("deutschland", "дойчланд");
        Add("fuhrer", "фюрер");
        Add("russland", "руссланд");
        Add("gott", "гот");
        Add("herrgott", "гергот");
        Add("halt", "хальт");
        Add("mensch", "менш");
        Add("sie", "зи");
        Add("hören", "хёрен");
        Add("hören sie", "хёрен зи");
        Add("soldat?", "зольдат?");
        Add("guten tag", "гутен таг");
        Add("guten morgen", "гутен моргэн");
        Add("was los?", "вас лос?");
        Add("wer bist du?", "вер бист ду?");
        Add("ich will gehen", "их виль геен");
        Add("ich will heim", "их виль хайм");
        Add("ich habe hunger", "их хабе хунгер");
        Add("ein guter mensch", "айн гутер менш");
        Add("nach hause", "нах хаузе");
        Add("hands up!", "хэнс ап!");

        return d;
    }

    /// <summary>Заминки в начале реплики (немецкая «запинка»).</summary>
    static readonly string[] Hesitations =
    {
        "Х-х... ", "Н-н... ", "П-па... ", "Г-г... ", "Ш-ш... ", "Т-т... ",
    };

    /// <summary>
    /// Прогнать реплику через немецкий акцент.
    /// Латинские слова превращаются в русскую фонетику, в начало изредка
    /// добавляется запинка. Русский текст не трогаем.
    /// </summary>
    public static string Apply(string text, int hesitationSeed = 0, float hesitationChance = 0.35f)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";

        string result = TransliterateLatinWords(text);

        if (hesitationChance > 0f && !result.StartsWith("...") && result.Length > 6)
        {
            // Детерминированно по seed: одна и та же реплика звучит одинаково.
            int hash = Math.Abs((text.GetHashCode() * 397) ^ (hesitationSeed * 31));
            float roll = (hash % 1000) / 1000f;
            if (roll < hesitationChance)
            {
                int idx = hash % Hesitations.Length;
                result = Hesitations[idx] + result;
            }
        }

        return result;
    }

    /// <summary>Замена латинских слов на русскую фонетику.</summary>
    static string TransliterateLatinWords(string text)
    {
        var sb = new StringBuilder(text.Length + 16);
        int i = 0;
        // Заглавную сохраняем только в начале фразы: по-немецки nouns пишут
        // с большой, но в русской записи «Дай брот» читается естественнее.
        bool atPhraseStart = true;
        while (i < text.Length)
        {
            char c = text[i];
            if (IsLatinLetter(c))
            {
                int start = i;
                while (i < text.Length && IsLatinLetter(text[i])) i++;
                string word = text.Substring(start, i - start);

                string translated = TranslateWord(word);
                if (translated.Length > 0)
                {
                    if (atPhraseStart) translated = char.ToUpperInvariant(translated[0]) + translated.Substring(1);
                    else translated = char.ToLowerInvariant(translated[0]) + translated.Substring(1);
                }

                sb.Append(translated);
                atPhraseStart = false;
                continue;
            }

            sb.Append(c);
            if (c == '.' || c == '!' || c == '?') atPhraseStart = true;
            else if (!char.IsWhiteSpace(c) && c != '"' && c != '«' && c != '—') atPhraseStart = false;
            i++;
        }
        return sb.ToString();
    }

    static bool IsLatinLetter(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == 'ä' || c == 'ö' ||
        c == 'ü' || c == 'ß' || c == 'Ä' || c == 'Ö' || c == 'Ü';

    /// <summary>Перевод одного немецкого слова.</summary>
    public static string TranslateWord(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;

        // Словарь: сначала точное совпадение, потом с отброшенными знаками.
        if (Words.TryGetValue(word, out string known)) return known;

        string key = StripAccents(word).ToLowerInvariant();
        if (Words.TryGetValue(key, out known)) return known;

        // Немецкое слово целиком: общие правила транслитерации.
        return Transliterate(key);
    }

    static string StripAccents(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            switch (c)
            {
                case 'ä': sb.Append('a'); break;
                case 'ö': sb.Append('o'); break;
                case 'ü': sb.Append('u'); break;
                case 'ß': sb.Append("ss"); break;
                case 'Ä': sb.Append('A'); break;
                case 'Ö': sb.Append('O'); break;
                case 'Ü': sb.Append('U'); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Общие правила: латиница → русская фонетика.</summary>
    static string Transliterate(string w)
    {
        if (string.IsNullOrEmpty(w)) return w;

        var sb = new StringBuilder(w.Length + 4);
        for (int i = 0; i < w.Length; i++)
        {
            if (i + 1 < w.Length)
            {
                string two = w.Substring(i, 2);
                switch (two)
                {
                    case "sch": sb.Append("ш"); i++; continue;
                    case "ch": sb.Append("х"); i++; continue;
                    case "ei":
                    case "ey": sb.Append("ай"); i++; continue;
                    case "ie": sb.Append("и"); i++; continue;
                    case "eu":
                    case "äu": sb.Append("ой"); i++; continue;
                    case "ck": sb.Append("к"); i++; continue;
                    case "qu": sb.Append("кв"); i++; continue;
                    case "tz": sb.Append("ц"); i++; continue;
                }
            }

            char c = w[i];
            switch (c)
            {
                case 'a': sb.Append('а'); break;
                case 'b': sb.Append('б'); break;
                case 'c': sb.Append('к'); break;
                case 'd': sb.Append('д'); break;
                case 'e': sb.Append('э'); break;
                case 'f': sb.Append('ф'); break;
                case 'g': sb.Append('г'); break;
                case 'h': sb.Append('х'); break;
                case 'i': sb.Append('и'); break;
                case 'j': sb.Append('й'); break;
                case 'k': sb.Append('к'); break;
                case 'l': sb.Append('л'); break;
                case 'm': sb.Append('м'); break;
                case 'n': sb.Append('н'); break;
                case 'o': sb.Append('о'); break;
                case 'p': sb.Append('п'); break;
                case 'r': sb.Append('р'); break;
                case 's': sb.Append('с'); break;
                case 't': sb.Append('т'); break;
                case 'u': sb.Append('у'); break;
                case 'v': sb.Append('ф'); break;
                case 'w': sb.Append('в'); break;
                case 'x': sb.Append("кс"); break;
                case 'y': sb.Append('й'); break;
                case 'z': sb.Append('ц'); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
