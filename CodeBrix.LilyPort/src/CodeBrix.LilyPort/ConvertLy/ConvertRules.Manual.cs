// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeBrix.LilyPort.ConvertLy; //was previously: python/convertrules.py;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The conversion rules whose bodies the generator will not translate — the ones that
/// loop, branch inside a replacement, or lean on a helper of their own.
/// </summary>
/// <remarks>
/// Hand-written, and each one against upstream's source at the line named above it.
/// The patterns are still VERBATIM: what is hand-written here is the CODE AROUND them.
/// The generated table in <c>ConvertRules.g.cs</c> names every rule in this file, so
/// one that is missing does not compile.
/// </remarks>
internal static partial class ConvertRules
{
    //convertrules.py:565-574
    private static string Rule_1_3_117(string s)
    {
        string RegularizeDollarReference(Match match)
            => RegularizeId(match.Groups[1].Value);

        string RegularizeAssignment(Match match)
            => "\n" + RegularizeId(match.Groups[1].Value) + " = ";

        s = PythonRegex.Sub("\\$([^\t\n ]+)", RegularizeDollarReference, s);
        s = PythonRegex.Sub("\n([^ \t\n]+)[ \t]*= *", RegularizeAssignment, s);
        return s;
    }

    //convertrules.py:577-589
    private static string Rule_1_3_120(string s)
    {
        string RegularizePaper(Match match) => RegularizeId(match.Groups[1].Value);

        s = PythonRegex.Sub("(paper_[a-z]+)", RegularizePaper, s);
        s = PythonRegex.Sub("sustainup", "sustainUp", s);
        s = PythonRegex.Sub("nobreak", "noBreak", s);
        s = PythonRegex.Sub("sustaindown", "sustainDown", s);
        s = PythonRegex.Sub("sostenutoup", "sostenutoUp", s);
        s = PythonRegex.Sub("sostenutodown", "sostenutoDown", s);
        s = PythonRegex.Sub("unachorda", "unaChorda", s);
        s = PythonRegex.Sub("trechorde", "treChorde", s);
        return s;
    }

    //convertrules.py:692-714
    private static string Rule_1_5_40(string s)
    {
        string Func(Match match)
        {
            //A dictionary, and upstream iterates it in INSERTION order (python 3.7+),
            //which matters: every key is substituted into the same text in turn.
            (string Key, string Value)[] breakDict =
            {
                ("Instrument_name", "instrument-name"),
                ("Left_edge_item", "left-edge"),
                ("Span_bar", "span-bar"),
                ("Breathing_sign", "breathing-sign"),
                ("Staff_bar", "staff-bar"),
                ("Clef_item", "clef"),
                ("Key_item", "key-signature"),
                ("Time_signature", "time-signature"),
                ("Custos", "custos"),
            };

            string props = match.Groups[1].Value;
            foreach ((string key, string value) in breakDict)
            {
                props = PythonRegex.Sub(key, value, props);
            }

            return PythonRegex.Format("breakAlignOrder = #'(%s)", props);
        }

        s = PythonRegex.Sub(
            "breakAlignOrder *= *#'\\(([a-z_\n\tA-Z ]+)\\)", Func, s);
        return s;
    }

    //convertrules.py:764-773
    private static string Rule_1_5_67(string s)
    {
        if (PythonRegex.Search("\\\\addlyrics", s).Success
            && !PythonRegex.Search("automaticMelismata", s).Success)
        {
            StdErr(PythonRegex.Format(NotSmart, "automaticMelismata"));
            StdErr("automaticMelismata is turned on by default since 1.5.67.");
            StdErr("\n");
            throw new FatalConversionError();
        }

        return s;
    }

    //convertrules.py:815-820, with subst_req_name at 810-811
    private static string Rule_1_7_1(string s)
    {
        string SubstReqName(Match match)
            => PythonRegex.Format(
                "(make-music-by-name '%sEvent)", RegularizeId(match.Groups[1].Value));

        s = PythonRegex.Sub("\\(ly-make-music *\"([A-Z][a-z_]+)_req\"\\)", SubstReqName, s);
        s = PythonRegex.Sub("Request_chord", "EventChord", s);
        return s;
    }

    //convertrules.py:859-876, with the subst_*_ev_name family at 823-856
    private static string Rule_1_7_2(string s)
    {
        s = PythonRegex.Sub(
            " *= *\\\\spanrequest *([^ ]+) *\"([^\"]+)\"", SubstDefinitionEvName, s);
        s = PythonRegex.Sub(
            "\\\\spanrequest *([^ ]+) *\"([^\"]+)\"", SubstInlineEvName, s);
        s = PythonRegex.Sub(
            " *= *\\\\commandspanrequest *([^ ]+) *\"([^\"]+)\"", SubstCspDefinition, s);
        s = PythonRegex.Sub(
            "\\\\commandspanrequest *([^ ]+) *\"([^\"]+)\"", SubstCspInline, s);
        s = PythonRegex.Sub("ly-id ", "ly-import ", s);

        s = PythonRegex.Sub(
            " *= *\\\\script \"([^\"]+)\"", " = #(make-articulation \"\\1\")", s);
        s = PythonRegex.Sub(
            "\\\\script \"([^\"]+)\"", "#(ly-export (make-articulation \"\\1\"))", s);
        return s;
    }

    /// <summary>convertrules.py's <c>spanner_subst</c>.</summary>
    private static readonly Dictionary<string, string> SpannerSubst
        = new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            { "text", "TextSpanEvent" },
            { "decrescendo", "DecrescendoEvent" },
            { "crescendo", "CrescendoEvent" },
            { "Sustain", "SustainPedalEvent" },
            { "slur", "SlurEvent" },
            { "UnaCorda", "UnaCordaEvent" },
            { "Sostenuto", "SostenutoEvent" },
        };

    /// <summary>convertrules.py's <c>subst_ev_name</c>.</summary>
    /// <param name="match">The match.</param>
    /// <returns>The replacement.</returns>
    /// <remarks>
    /// ⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14). Upstream indexes
    /// <c>spanner_subst</c> directly — <c>mtype = spanner_subst[match.group(2)]</c> —
    /// so a <c>\spanrequest</c> naming a spanner the table does not hold raises
    /// <c>KeyError</c> and convert-ly dies with a python traceback, having written
    /// nothing. The pattern that selects the text is <c>"([^"]+)"</c>, which accepts any
    /// name at all, so this is a crash on a state the rule's own interface allows, and
    /// the tool exists to salvage documents rather than to fall over on them. The port
    /// leaves a name it does not know EXACTLY as written and converts the rest of the
    /// document; every name upstream knows converts identically.
    /// </remarks>
    private static string SubstEvName(Match match)
    {
        string stype = PythonRegex.Search("start", match.Groups[1].Value).Success
            ? "START"
            : "STOP";
        return SpannerSubst.TryGetValue(match.Groups[2].Value, out string mtype)
            ? PythonRegex.Format("(make-span-event '%s %s)", mtype, stype)
            : null;
    }

    private static string SubstDefinitionEvName(Match match)
    {
        string inner = SubstEvName(match);
        return inner == null ? match.Value : PythonRegex.Format(" = #%s", inner);
    }

    private static string SubstInlineEvName(Match match)
    {
        string inner = SubstEvName(match);
        return inner == null ? match.Value : PythonRegex.Format("#(ly-export %s)", inner);
    }

    private static string SubstCspDefinition(Match match)
    {
        string inner = SubstEvName(match);
        return inner == null
            ? match.Value
            : PythonRegex.Format(" = #(make-event-chord (list %s))", inner);
    }

    private static string SubstCspInline(Match match)
    {
        string inner = SubstEvName(match);
        return inner == null
            ? match.Value
            : PythonRegex.Format("#(ly-export (make-event-chord (list %s)))", inner);
    }

    //convertrules.py:879-917
    private static string Rule_1_7_3(string s)
    {
        s = PythonRegex.Sub("\\(ly-", "(ly:", s);

        string[] changed =
        {
            "duration\\?",
            "font-metric\\?",
            "molecule\\?",
            "moment\\?",
            "music\\?",
            "pitch\\?",
            "make-duration",
            "music-duration-length",
            "duration-log",
            "duration-dotcount",
            "intlog2",
            "duration-factor",
            "transpose-key-alist",
            "get-system",
            "get-broken-into",
            "get-original",
            "set-point-and-click!",
            "make-moment",
            "make-pitch",
            "pitch-octave",
            "pitch-alteration",
            "pitch-notename",
            "pitch-semitones",
            "pitch<\\?",
            "dir\\?",
            "music-duration-compress",
            "set-point-and-click!",
        };

        string origre = PythonRegex.Format("\\b(%s)", string.Join("|", changed));

        s = PythonRegex.Sub(origre, "ly:\\1", s);
        s = PythonRegex.Sub("set-point-and-click!", "set-point-and-click", s);
        return s;
    }

    //convertrules.py:937-955
    private static string Rule_1_7_6(string s)
    {
        string[] kws =
        {
            "arpeggio", "sustainDown", "sustainUp", "f", "p", "pp", "ppp",
            "fp", "ff", "mf", "mp", "sfz",
        };

        string origstr = string.Join("|", kws);
        s = PythonRegex.Sub(
            PythonRegex.Format("([^_^-])\\\\(%s)\\b", origstr), "\\1-\\\\\\2", s);
        return s;
    }

    //convertrules.py:1330-1334
    private static string ConvRelative(string s)
    {
        if (PythonRegex.Search("\\\\relative", s).Success)
        {
            s = "#(ly:set-option 'old-relative)\n" + s;
        }

        return s;
    }

    //convertrules.py:1277-1286
    private static string ArticulationSubstitute(string s)
    {
        s = PythonRegex.Sub(
            "([^-])\\[ *(\\\\?\\)?[a-z]+[,']*[!?]?[0-9:]*\\.*)", "\\1 \\2[", s);
        s = PythonRegex.Sub(
            "([^-])\\\\\\) *([a-z]+[,']*[!?]?[0-9:]*\\.*)", "\\1 \\2\\\\)", s);
        s = PythonRegex.Sub(
            "([^-\\\\])\\) *([a-z]+[,']*[!?]?[0-9:]*\\.*)", "\\1 \\2)", s);
        s = PythonRegex.Sub(
            "([^-])\\\\! *([a-z]+[,']*[!?]?[0-9:]*\\.*)", "\\1 \\2\\\\!", s);
        return s;
    }

    //convertrules.py:1075-1193. The chord-syntax rewriter: it strips everything that
    //is not a note out of the old `<...>' chord, remembering each removed marking, and
    //puts the markings back after the new `<<...>>'.
    private static string SubChord(Match m)
    {
        string s = m.Groups[1].Value;

        string origstr = PythonRegex.Format("<%s>", s);
        if (PythonRegex.Search("\\\\\\\\", s).Success) { return origstr; }

        if (PythonRegex.Search("\\\\property", s).Success) { return origstr; }

        if (PythonRegex.MatchAt("^\\s*\\)?\\s*\\\\[a-zA-Z]+", s).Success) { return origstr; }

        List<string> durs = new List<string>();

        string SubDurs(Match inner)
        {
            durs.Add(inner.Groups[2].Value);
            return inner.Groups[1].Value;
        }

        s = PythonRegex.Sub("([a-z]+[,'!? ]*)([0-9]+\\.*)", SubDurs, s);
        string durStr = string.Empty;

        foreach (string d in durs)
        {
            if (durStr.Length == 0) { durStr = d; }

            if (!string.Equals(durStr, d, System.StringComparison.Ordinal))
            {
                return PythonRegex.Format("<%s>", m.Groups[1].Value);
            }
        }

        List<string> pslurStrs = new List<string> { string.Empty };
        List<string> dyns = new List<string> { string.Empty };
        List<string> slurStrs = new List<string> { string.Empty };

        string lastStr = string.Empty;
        while (!string.Equals(lastStr, s, System.StringComparison.Ordinal))
        {
            lastStr = s;

            string SubTremolos(Match inner)
            {
                string tr = inner.Groups[2].Value;
                if (!slurStrs.Contains(tr)) { slurStrs.Add(tr); }

                return inner.Groups[1].Value;
            }

            s = PythonRegex.Sub("([a-z]+[',!? ]*)(:[0-9]+)", SubTremolos, s);

            string SubDynEnd(Match inner)
            {
                dyns.Add(" \\!");
                return " " + inner.Groups[2].Value;
            }

            s = PythonRegex.Sub("(\\\\!)\\s*([a-z]+)", SubDynEnd, s);

            string SubSlurs(Match inner)
            {
                if (!slurStrs.Contains("-)")) { slurStrs.Add(")"); }

                return inner.Groups[1].Value;
            }

            string SubPSlurs(Match inner)
            {
                if (!slurStrs.Contains("-\\)")) { slurStrs.Add("\\)"); }

                return inner.Groups[1].Value;
            }

            s = PythonRegex.Sub("\\)[ ]*([a-z]+)", SubSlurs, s);
            s = PythonRegex.Sub("\\\\\\)[ ]*([a-z]+)", SubPSlurs, s);

            string SubBeginSlurs(Match inner)
            {
                if (!slurStrs.Contains("-(")) { slurStrs.Add("("); }

                return inner.Groups[1].Value;
            }

            s = PythonRegex.Sub("([a-z]+[,'!?0-9 ]*)\\(", SubBeginSlurs, s);

            string SubBeginPSlurs(Match inner)
            {
                if (!slurStrs.Contains("-\\(")) { slurStrs.Add("\\("); }

                return inner.Groups[1].Value;
            }

            s = PythonRegex.Sub("([a-z]+[,'!?0-9 ]*)\\\\\\(", SubBeginPSlurs, s);

            //⚠ PORTED FAITHFULLY, INCLUDING A BRANCH THAT CANNOT FIRE. The second call
            //below matches `\!' or `-\!', and this function then compares the matched
            //text with the string "-?\\!" -- which is the PATTERN, not anything the
            //pattern can match -- so a bare dynamic end inside an old chord is deleted
            //and never re-added. It is not fixed here: FR14's bar is a DEMONSTRABLE
            //defect, and what upstream meant a bare `\!' to become is not demonstrable
            //from the code (the `\!' followed by a note IS handled, by SubDynEnd above).
            //Changing it would also break byte-parity with convert-ly, which is this
            //component's gate. Reported in the wave's STATUS file for a ruling instead.
            string SubDyns(Match inner)
            {
                string text = inner.Value;
                if (string.Equals(text, "@STARTCRESC@", System.StringComparison.Ordinal))
                {
                    slurStrs.Add("\\<");
                }
                else if (string.Equals(
                    text, "@STARTDECRESC@", System.StringComparison.Ordinal))
                {
                    slurStrs.Add("\\>");
                }
                else if (string.Equals(text, "-?\\\\!", System.StringComparison.Ordinal))
                {
                    slurStrs.Add("\\!");
                }

                return string.Empty;
            }

            s = PythonRegex.Sub("@STARTCRESC@", SubDyns, s);
            s = PythonRegex.Sub("-?\\\\!", SubDyns, s);

            string SubArticulations(Match inner)
            {
                string a = inner.Groups[1].Value;
                if (!slurStrs.Contains(a)) { slurStrs.Add(a); }

                return string.Empty;
            }

            s = PythonRegex.Sub("([_^-]\\@ACCENT\\@)", SubArticulations, s);
            s = PythonRegex.Sub("([_^-]\\\\[a-z]+)", SubArticulations, s);
            s = PythonRegex.Sub("([_^-][>_.+|^-])", SubArticulations, s);
            s = PythonRegex.Sub("([_^-]\"[^\"]+\")", SubArticulations, s);

            string SubPslurs(Match inner)
            {
                slurStrs.Add(" \\)");
                return inner.Groups[1].Value;
            }

            s = PythonRegex.Sub("\\\\\\)[ ]*([a-z]+)", SubPslurs, s);
        }

        string suffix = string.Concat(slurStrs) + string.Concat(pslurStrs)
            + string.Concat(dyns);

        return PythonRegex.Format(
            "@STARTCHORD@%s@ENDCHORD@%s%s", s, durStr, suffix);
    }

    //convertrules.py:1196-1230
    private static string SubChords(string s)
    {
        const string simend = ">";
        const string simstart = "<";
        const string chordstart = "<<";
        const string chordend = ">>";
        const string markerStr = "%% new-chords-done %%";

        if (PythonRegex.Search(markerStr, s).Success) { return s; }

        s = PythonRegex.Sub("<<", "@STARTCHORD@", s);
        s = PythonRegex.Sub(">>", "@ENDCHORD@", s);

        s = PythonRegex.Sub("\\\\<", "@STARTCRESC@", s);
        s = PythonRegex.Sub("\\\\>", "@STARTDECRESC@", s);
        s = PythonRegex.Sub("([_^-])>", "\\1@ACCENT@", s);
        s = PythonRegex.Sub("<([^<>{}]+)>", SubChord, s);

        //Add dash: -[, so that [<<a b>> c d] becomes <<a b>>-[ c d] and gets skipped
        //by articulation_substitute.
        s = PythonRegex.Sub(
            "\\[ *(@STARTCHORD@[^@]+@ENDCHORD@[0-9.]*)", "\\1-[", s);
        s = PythonRegex.Sub(
            "\\\\! *(@STARTCHORD@[^@]+@ENDCHORD@[0-9.]*)", "\\1-\\\\!", s);

        s = PythonRegex.Sub("<([^?])", PythonRegex.Format("%s\\1", simstart), s);
        s = PythonRegex.Sub(">([^?])", PythonRegex.Format("%s\\1", simend), s);
        s = PythonRegex.Sub("@STARTCRESC@", "\\\\<", s);
        s = PythonRegex.Sub("@STARTDECRESC@", "\\\\>", s);
        s = PythonRegex.Sub("\\\\context *Voice *@STARTCHORD@", "@STARTCHORD@", s);
        s = PythonRegex.Sub("@STARTCHORD@", chordstart, s);
        s = PythonRegex.Sub("@ENDCHORD@", chordend, s);
        s = PythonRegex.Sub("@ACCENT@", ">", s);
        return s;
    }

    //convertrules.py:1241-1274
    private static string TextMarkup(string s)
    {
        string result = string.Empty;

        //Find the beginning of each markup.
        Match match = MarkupStart.Match(s);
        while (match.Success)
        {
            result = result + s.Substring(0, match.Groups[1].Index + match.Groups[1].Length)
                + " \\markup";
            s = s.Substring(match.Groups[2].Index + match.Groups[2].Length);

            //Count matching parentheses to find the end of the current markup.
            int nestingLevel = 0;
            int markupEnd = 0;
            foreach (Match par in PythonRegex.Compile("[()]").Matches(s))
            {
                nestingLevel += par.Value == "(" ? 1 : -1;
                if (nestingLevel == 0)
                {
                    markupEnd = par.Index + par.Length;
                    break;
                }
            }

            //The full markup in old syntax.
            string markup = s.Substring(0, markupEnd);

            //Modify to new syntax.
            markup = Musicglyph.Replace(
                markup, PythonRegex.TranslateReplacement("{\\\\musicglyph"));
            markup = Columns.Replace(markup, PythonRegex.TranslateReplacement("{"));
            markup = SubmarkupStart.Replace(
                markup, PythonRegex.TranslateReplacement("{\\\\\\1"));
            markup = Leftpar.Replace(markup, PythonRegex.TranslateReplacement("{"));
            markup = Rightpar.Replace(markup, PythonRegex.TranslateReplacement("}"));

            result += markup;

            //Find next markup.
            s = s.Substring(markupEnd);
            match = MarkupStart.Match(s);
        }

        return result + s;
    }

    //convertrules.py:1295-1327
    private static string SmarterArticulationSubst(string s)
    {
        string result = string.Empty;

        //Find the beginning of next string or Scheme expression.
        Match match = StringOrScheme.Match(s);
        while (match.Success)
        {
            //Convert the preceding LilyPond code.
            string previousChunk = s.Substring(0, match.Index);
            result += ArticulationSubstitute(previousChunk);
            if (match.Groups[1].Success)
            {
                //Found a string: copy it to output.
                result += match.Groups[1].Value;
                s = s.Substring(match.Groups[1].Index + match.Groups[1].Length);
            }
            else
            {
                //Found a Scheme expression: count matching parentheses to find its end.
                s = s.Substring(match.Index);
                int nestingLevel = 0;
                int schemeEnd = 0;
                foreach (Match par in PythonRegex.Compile("[()]").Matches(s))
                {
                    nestingLevel += par.Value == "(" ? 1 : -1;
                    if (nestingLevel == 0)
                    {
                        schemeEnd = par.Index + par.Length;
                        break;
                    }
                }

                //Copy the Scheme expression to output.
                result += s.Substring(0, schemeEnd);
                s = s.Substring(schemeEnd);
            }

            //Find next string or Scheme expression.
            match = StringOrScheme.Match(s);
        }

        //Convert the remainder of the file.
        return result + ArticulationSubstitute(s);
    }

    //convertrules.py:1337-1347
    private static string Rule_1_9_0(string s)
    {
        s = PythonRegex.Sub("#'\\(\\)", "@SCM_EOL@", s);
        s = ConvRelative(s);
        s = SubChords(s);

        s = TextMarkup(s);
        s = SmarterArticulationSubst(s);
        s = PythonRegex.Sub("@SCM_EOL@", "#'()", s);
        return s;
    }

    //convertrules.py:1460-1503
    private static string Rule_1_9_7(string s)
    {
        //⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14), the same shape as the
        //one in SubstEvName: upstream indexes its alteration table directly, so a
        //`(ly:make-pitch a b 3)' -- which its own `([0-9-]+)' pattern accepts -- raises
        //KeyError and convert-ly dies having written nothing. An alteration outside the
        //table is left exactly as written here and the rest of the document converts.
        string SubAlteration(Match m)
        {
            string alt = m.Groups[3].Value;
            switch (alt)
            {
                case "-1": alt = "FLAT"; break;
                case "-2": alt = "DOUBLE-FLAT"; break;
                case "0": alt = "NATURAL"; break;
                case "1": alt = "SHARP"; break;
                case "2": alt = "DOUBLE-SHARP"; break;
                default: return m.Value;
            }

            return PythonRegex.Format(
                "(ly:make-pitch %s %s %s)",
                m.Groups[1].Value, m.Groups[2].Value, alt);
        }

        s = PythonRegex.Sub(
            "\\(ly:make-pitch *([0-9-]+) *([0-9-]+) *([0-9-]+) *\\)", SubAlteration, s);

        s = PythonRegex.Sub("ly:verbose", "ly:get-option 'verbose", s);

        Match found = PythonRegex.Search(
            "\\\\outputproperty #([^#]+)[\t\n ]*#'([^ ]+)", s);
        if (found.Success)
        {
            StdErr(PythonRegex.Format(
                "\\outputproperty found,\nPlease hand-edit, using\n\n"
                + "  \\applyoutput #(outputproperty-compatibility %s '%s "
                + "<GROB PROPERTY VALUE>)\n\nas a substitution text",
                found.Groups[1].Value, found.Groups[2].Value));
            throw new FatalConversionError();
        }

        if (PythonRegex.Search("ly:(make-pitch|pitch-alteration)", s).Success
            || PythonRegex.Search("keySignature", s).Success)
        {
            StdErr(PythonRegex.Format(NotSmart, "pitches"));
            StdErr("The alteration field of Scheme pitches was multiplied by 2\n"
                + "to support quarter tone accidentals.  You must update the "
                + "following constructs manually:\n\n"
                + "* calls of ly:make-pitch and ly:pitch-alteration\n"
                + "* keySignature settings made with \\property\n");
            throw new FatalConversionError();
        }

        return s;
    }

    //convertrules.py:1539-1555
    private static string Rule_2_1_4(string s)
    {
        string Func(Match match)
        {
            string c = match.Groups[1].Value;
            string b = match.Groups[2].Value;

            if (b == "t")
            {
                return c == "Score"
                    ? string.Empty
                    : PythonRegex.Format(
                        " \\property %s.melismaBusyProperties \\unset", c);
            }

            //Upstream asserts b == 'f' here; the pattern's own [ft] guarantees it.
            return PythonRegex.Format(
                "\\property %s.melismaBusyProperties = #'(melismaBusy)", c);
        }

        s = PythonRegex.Sub(
            "\\\\property ([a-zA-Z]+)\\s*\\.\\s*automaticMelismata\\s*=\\s*##([ft])",
            Func, s);
        return s;
    }

    //convertrules.py:1570-1595
    private static string Rule_2_1_11(string s)
    {
        s = PythonRegex.Sub(
            "\\\\include\\s*\"paper([0-9]+)(-init)?.ly\"", "#(set-staff-size \\1)", s);

        //⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14), the alteration-table
        //shape again: a log outside -3..-1 that is still negative raises KeyError
        //upstream. Left as written here.
        string SubNote(Match match)
        {
            string dur;
            int log = PythonRegex.ToInt(match.Groups[1].Value);
            int dots = PythonRegex.ToInt(match.Groups[2].Value);

            if (log >= 0)
            {
                dur = PythonRegex.Format("%d", 1 << log);
            }
            else
            {
                switch (log)
                {
                    case -1: dur = "breve"; break;
                    case -2: dur = "longa"; break;
                    case -3: dur = "maxima"; break;
                    default: return match.Value;
                }
            }

            dur += new string('.', dots);

            return PythonRegex.Format("\\note #\"%s\" #%s", dur, match.Groups[3].Value);
        }

        s = PythonRegex.Sub(
            "\\\\note\\s+#([0-9-]+)\\s+#([0-9]+)\\s+#([0-9.-]+)", SubNote, s);
        return s;
    }

    //convertrules.py:1627-1644
    private static string Rule_2_1_16(string s)
    {
        //⚠ FR14, the table-index shape once more: `([0-9-]+)' accepts numbers the
        //table has no entry for, and upstream raises KeyError on them.
        string SubAcc(Match m)
        {
            switch (m.Groups[1].Value)
            {
                case "4": return "\\doublesharp";
                case "3": return "\\threeqsharp";
                case "2": return "\\sharp";
                case "1": return "\\semisharp";
                case "0": return "\\natural";
                case "-1": return "\\semiflat";
                case "-2": return "\\flat";
                case "-3": return "\\threeqflat";
                case "-4": return "\\doubleflat";
                default: return m.Value;
            }
        }

        s = PythonRegex.Sub("\\\\musicglyph\\s*#\"accidentals-([0-9-]+)\"", SubAcc, s);
        return s;
    }

    //convertrules.py:1768-1797
    private static string Rule_2_1_23(string s)
    {
        string SubstInTrans(Match match)
        {
            string text = match.Value;
            text = PythonRegex.Sub(
                "\\s([a-zA-Z]+)\\s*\\\\override", " \\\\override \\1", text);
            text = PythonRegex.Sub(
                "\\s([a-zA-Z]+)\\s*\\\\set", " \\\\override \\1", text);
            text = PythonRegex.Sub(
                "\\s([a-zA-Z]+)\\s*\\\\revert", " \\\\revert \\1", text);
            return text;
        }

        s = PythonRegex.Sub("\\\\(translator|with)\\s*{[^}]+}", SubstInTrans, s);

        string SubAbs(Match m)
        {
            string context = m.Groups["context"].Value;
            context = m.Groups["context"].Success && context.Length > 0
                ? PythonRegex.Format(" '%s", context.Substring(0, context.Length - 1))
                : string.Empty;

            return PythonRegex.Format(
                "#(override-auto-beam-setting %s %s %s%s)",
                m.Groups["prop"].Value, m.Groups["num"].Value,
                m.Groups["den"].Value, context);
        }

        s = PythonRegex.Sub(
            "\\\\override\\s*(?P<context>[a-zA-Z]+\\s*\\.\\s*)?autoBeamSettings"
            + "\\s*#(?P<prop>[^=]+)\\s*=\\s*#\\(ly:make-moment\\s+(?P<num>\\d+)"
            + "\\s+(?P<den>\\d)\\s*\\)",
            SubAbs, s);
        return s;
    }

    //convertrules.py:1852-1873
    private static string Rule_2_1_27(string s)
    {
        string Subst(Match m)
        {
            int value = PythonRegex.ToInt(m.Groups[2].Value);

            //python's divmod FLOORS, so a negative transposition takes the octave
            //DOWN and leaves a non-negative remainder; C#'s / and % truncate toward
            //zero and would give the wrong octave and a negative note.
            int o = (int)System.Math.Floor(value / 12.0);
            int g = value - (o * 12);

            int[] scale = { 0, 2, 4, 5, 7, 9, 11, 12 };
            List<int> lowerPitches = new List<int>();
            foreach (int x in scale)
            {
                if (x <= g) { lowerPitches.Add(x); }
            }

            int index = lowerPitches.Count - 1;
            int a = g - lowerPitches[lowerPitches.Count - 1];

            string note = "cdefgab"[index].ToString();
            note += new[] { "eses", "es", "", "is", "isis" }[a + 2];
            o += 1;                 //c' is octave 0
            if (o < 0)
            {
                note += new string(',', -o);
            }
            else if (o > 0)
            {
                note += new string('\'', o);
            }

            return PythonRegex.Format("\\transposition %s ", note);
        }

        s = PythonRegex.Sub(
            "\\\\set ([A-Za-z]+\\s*\\.\\s*)?transposing\\s*=\\s*#([-0-9]+)", Subst, s);
        return s;
    }

    //convertrules.py:1953-1973
    private static string Rule_2_3_2(string s)
    {
        if (PythonRegex.Search("textheight", s).Success)
        {
            StdErr(PythonRegex.Format(NotSmart, "textheight"));
            StdErr(UpdateManually);
            StdErr("Page layout has been changed, using paper size and margins.\n"
                + "textheight is no longer used.\n");
        }

        s = PythonRegex.Sub("\\\\OrchestralScoreContext", "\\\\Score", s);

        string Func(Match m)
        {
            string name = m.Groups[1].Value;
            return name != "RemoveEmptyStaff"
                && name != "AncientRemoveEmptyStaffContext"
                && name != "EasyNotation"
                    ? "\\" + name
                    : m.Value;
        }

        s = PythonRegex.Sub("\\\\([a-zA-Z]+)Context\\b", Func, s);
        s = PythonRegex.Sub("ly:paper-lookup", "ly:output-def-lookup", s);
        return s;
    }

    //convertrules.py:2104-2110
    private static string Rule_2_3_24(string s)
    {
        string Sub(Match m) => RegularizeId(m.Groups[1].Value);

        s = PythonRegex.Sub(
            "(maintainer_email|maintainer_web|midi_stuff|gourlay_maxmeasures)", Sub, s);
        return s;
    }

    //convertrules.py:2315-2342
    private static string Rule_2_7_13(string s)
    {
        string Subber(Match match)
        {
            string newkey;
            switch (match.Groups[3].Value)
            {
                case "spacing-procedure": newkey = "springs-and-rods"; break;
                case "after-line-breaking-callback": newkey = "after-line-breaking"; break;
                case "before-line-breaking-callback": newkey = "before-line-breaking"; break;
                default: newkey = "stencil"; break;
            }

            string what = match.Groups[1].Value;
            string grob = match.Groups[2].Value;

            if (what == "revert")
            {
                return PythonRegex.Format("revert %s #'callbacks %% %s\n", grob, newkey);
            }

            //Upstream raises RuntimeError if the first group is neither; the pattern's
            //own alternation makes that unreachable.
            return PythonRegex.Format("override %s #'callbacks #'%s", grob, newkey);
        }

        s = PythonRegex.Sub(
            "(override|revert)\\s*([a-zA-Z.]+)\\s*#'(spacing-procedure"
            + "|after-line-breaking-callback|before-line-breaking-callback"
            + "|print-function)",
            Subber, s);

        if (PythonRegex.Search("bar-size-procedure", s).Success)
        {
            StdErr(PythonRegex.Format(NotSmart, "bar-size-procedure"));
        }

        if (PythonRegex.Search("space-function", s).Success)
        {
            StdErr(PythonRegex.Format(NotSmart, "space-function"));
        }

        if (PythonRegex.Search("verticalAlignmentChildCallback", s).Success)
        {
            StdErr("verticalAlignmentChildCallback has been deprecated");
            StdErr("\n");
        }

        return s;
    }

    //convertrules.py:2385-2393
    private static string Rule_2_7_22(string s)
    {
        string SubSyms(Match m)
        {
            //python's str.split() with no argument splits on RUNS of whitespace and
            //drops empty pieces, which String.Split(char) does not.
            string[] syms = m.Groups[1].Value.Split(
                (char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            List<string> tags = new List<string>();
            foreach (string sym in syms)
            {
                tags.Add(PythonRegex.Format("\\tag #'%s", sym));
            }

            return string.Join(" ", tags);
        }

        s = PythonRegex.Sub("\\\\tag #'\\(([^)]+)\\)", SubSyms, s);
        return s;
    }

    //convertrules.py:2410-2418
    private static string Rule_2_7_29(string s)
    {
        string[] properties =
        {
            "beamed-lengths", "beamed-minimum-free-lengths", "lengths",
            "beamed-extreme-minimum-free-lengths",
        };

        foreach (string a in properties)
        {
            s = PythonRegex.Sub(
                PythonRegex.Format("\\\\override\\s+Stem\\s+#'%s", a),
                PythonRegex.Format("\\\\override Stem #'details #'%s", a),
                s);
        }

        return s;
    }

    //convertrules.py:2438-2479
    private static string Rule_2_7_32(string s)
    {
        (string From, string To)[] identifierSubs =
        {
            ("inputencoding", "input-encoding"),
            ("printpagenumber", "print-page-number"),
            ("outputscale", "output-scale"),
            ("betweensystemspace", "between-system-space"),
            ("betweensystempadding", "between-system-padding"),
            ("pagetopspace", "page-top-space"),
            ("raggedlastbottom", "ragged-last-bottom"),
            ("raggedright", "ragged-right"),
            ("raggedlast", "ragged-last"),
            ("raggedbottom", "ragged-bottom"),
            ("aftertitlespace", "after-title-space"),
            ("beforetitlespace", "before-title-space"),
            ("betweentitlespace", "between-title-space"),
            ("topmargin", "top-margin"),
            ("bottommargin", "bottom-margin"),
            ("headsep", "head-separation"),
            ("footsep", "foot-separation"),
            ("rightmargin", "right-margin"),
            ("leftmargin", "left-margin"),
            ("printfirstpagenumber", "print-first-page-number"),
            ("firstpagenumber", "first-page-number"),
            ("hsize", "paper-width"),
            ("vsize", "paper-height"),
            ("horizontalshift", "horizontal-shift"),
            ("staffspace", "staff-space"),
            ("linethickness", "line-thickness"),
            ("ledgerlinethickness", "ledger-line-thickness"),
            ("blotdiameter", "blot-diameter"),
            ("staffheight", "staff-height"),
            ("linewidth", "line-width"),
            ("annotatespacing", "annotate-spacing"),
        };

        foreach ((string a, string b) in identifierSubs)
        {
            s = PythonRegex.Sub(a, b, s);
        }

        return s;
    }

    //convertrules.py:2550-2578
    private static string Rule_2_9_16(string s)
    {
        string SubTempo(Match m)
        {
            int dur = PythonRegex.ToInt(m.Groups[1].Value);
            int dots = m.Groups[2].Value.Length;
            int count = PythonRegex.ToInt(m.Groups[3].Value);

            int log2 = 0;
            while (dur > 1)
            {
                dur /= 2;
                log2 += 1;
            }

            int den = (1 << dots) * (1 << log2);
            int num = (1 << (dots + 1)) - 1;

            return PythonRegex.Format(
                "\n  \\midi {\n    \\context {\n      \\Score\n"
                + "      tempoWholesPerMinute = #(ly:make-moment %d %d)\n"
                + "      }\n    }\n\n",
                num * count, den);
        }

        s = PythonRegex.Sub(
            "\\\\midi\\s*{\\s*\\\\tempo ([0-9]+)\\s*([.]*)\\s*=\\s*([0-9]+)\\s*}",
            SubTempo, s);
        return s;
    }

    //convertrules.py:2621-2644
    private static string Rule_2_11_6(string s)
    {
        //⚠ FR14, the table-index shape: `4-idx' outside 0..8 is an IndexError upstream
        //(and a NEGATIVE index silently reads from the far end of the list, which is
        //worse than an error). The port leaves such a glyph name as written.
        string SubAccName(Match m)
        {
            string[] names =
            {
                "accidentals.doublesharp",
                "accidentals.sharp.slashslash.stemstemstem",
                "accidentals.sharp",
                "accidentals.sharp.slashslash.stem",
                "accidentals.natural",
                "accidentals.mirroredflat",
                "accidentals.flat",
                "accidentals.mirroredflat.flat",
                "accidentals.flatflat",
            };

            int idx = PythonRegex.ToInt(m.Groups[1].Value.Replace("M", "-"));
            int index = 4 - idx;
            return index >= 0 && index < names.Length ? names[index] : m.Value;
        }

        s = PythonRegex.Sub("accidentals[.](M?[-0-9]+)", SubAccName, s);
        s = PythonRegex.Sub(
            "(KeySignature|Accidental[A-Za-z]*)\\s*#'style\\s*=\\s*#'([a-z]+)",
            "\\1 #'glyph-name-alist = #alteration-\\2-glyph-name-alist", s);

        //FIXME: standard vs default, alteration-FOO vs FOO-alteration
        s = s.Replace(
            "alteration-default-glyph-name-alist",
            "standard-alteration-glyph-name-alist");
        return s;
    }

    //convertrules.py:2684-2714
    private static string Rule_2_11_15(string s)
    {
        string SubEdgeHeight(Match m)
        {
            string text = string.Empty;
            foreach ((string var, string h) in new[]
            {
                ("left", m.Groups[3].Value),
                ("right", m.Groups[4].Value),
            })
            {
                //python: `if h and float(h)' -- a present value that is not zero.
                if (h.Length > 0
                    && double.Parse(
                        h, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture) != 0)
                {
                    string once = m.Groups[1].Success ? m.Groups[1].Value : string.Empty;
                    string context = m.Groups[2].Success
                        ? m.Groups[2].Value
                        : string.Empty;

                    text += PythonRegex.Format(
                        "%s \\override %sTextSpanner #'bound-details #'%s #'text = "
                        + "\\markup { \\draw-line #'(0 . %s) }",
                        once, context, var, h);

                    text += "\n";
                }
            }

            return text;
        }

        s = PythonRegex.Sub(
            "(\\\\once)?\\s*\\\\override\\s*([a-zA-Z]+\\s*[.]\\s*)?TextSpanner"
            + "\\s*#'edge-height\\s*=\\s*#'\\(\\s*([0-9.-]+)\\s+[.]\\s+([0-9.-]+)\\s*\\)",
            SubEdgeHeight, s);

        if (PythonRegex.Search("#'forced-distance", s).Success)
        {
            StdErr(PythonRegex.Format(NotSmart, "VerticalAlignment #'forced-distance"));
            StdErr("Use the `alignment-offsets' sub-property of\n");
            StdErr("NonMusicalPaperColumn #'line-break-system-details\n");
            StdErr("to set fixed distances between staves.\n");
        }

        return s;
    }

    //convertrules.py:2771-2800
    private static string Rule_2_11_50(string s)
    {
        //warning 1/2: metronomeMarkFormatter uses text markup as second argument
        if (PythonRegex.Search("metronomeMarkFormatter", s).Success)
        {
            StdErr(PythonRegex.Format(NotSmart, "metronomeMarkFormatter"));
            StdErr("metronomeMarkFormatter got an additional text argument.\n");
            StdErr(PythonRegex.Format(
                "The function assigned to Score.metronomeMarkFunction now uses the "
                + "signature\n%s",
                "\t(format-metronome-markup text dur count context)\n"));
        }

        //warning 2/2: fret diagram properties moved to fret-diagram-details
        string[] fretProps =
        {
            "barre-type", "dot-color", "dot-radius", "finger-code", "fret-count",
            "label-dir", "number-type", "string-count", "xo-font-magnification",
            "mute-string", "open-string", "orientation",
        };

        foreach (string prop in fretProps)
        {
            if (PythonRegex.Search(prop, s).Success)
            {
                StdErr(PythonRegex.Format(
                    NotSmart,
                    PythonRegex.Format("%s in fret-diagram properties", prop)));
                StdErr(PythonRegex.Format("Use %s\n", "fret-diagram-details"));
            }
        }

        return s;
    }

    //convertrules.py:3068-3107
    private static string Rule_2_13_29(string s)
    {
        //⚠ FR14, the table-index shape: `acc[a-zA-Z]+' accepts names the table has no
        //entry for, and upstream raises KeyError on them.
        string SubAcc(Match m)
        {
            switch (m.Groups[1].Value)
            {
                case "Dot": return "\"accordion.dot\"";
                case "Discant": return "\"accordion.discant\"";
                case "Bayanbase": return "\"accordion.bayanbass\"";
                case "Stdbase": return "\"accordion.stdbass\"";
                case "Freebase": return "\"accordion.freebass\"";
                case "OldEE": return "\"accordion.oldEE\"";
                default: return m.Value;
            }
        }

        s = PythonRegex.Sub("\"accordion\\.acc([a-zA-Z]+)\"", SubAcc, s);

        if (PythonRegex.Search("overrideBeamSettings", s).Success)
        {
            StdErr(PythonRegex.Format(NotSmart, "\\overrideBeamSettings"));
            StdErr("Use \\set beamExceptions or \\overrideTimeSignatureSettings.\n");
            StdErr(UpdateManually);
        }

        if (PythonRegex.Search("revertBeamSettings", s).Success)
        {
            StdErr(PythonRegex.Format(NotSmart, "\\revertBeamSettings"));
            StdErr("Use \\set beamExceptions or \\revertTimeSignatureSettings.\n");
            StdErr(UpdateManually);
        }

        if (PythonRegex.Search("beamSettings", s).Success)
        {
            StdErr(PythonRegex.Format(NotSmart, "beamSettings"));
            StdErr("Use baseMoment, beatStructure, and beamExceptions.\n");
            StdErr(UpdateManually);
        }

        if (PythonRegex.Search("beatLength", s).Success)
        {
            StdErr(PythonRegex.Format(NotSmart, "beatLength"));
            StdErr("Use baseMoment and beatStructure.\n");
            StdErr(UpdateManually);
        }

        if (PythonRegex.Search("setBeatGrouping", s).Success)
        {
            StdErr(PythonRegex.Format(NotSmart, "setbeatGrouping"));
            StdErr("Use baseMoment and beatStructure.\n");
            StdErr(UpdateManually);
        }

        return s;
    }

    //convertrules.py:3202-3250
    private static string Rule_2_13_46(string s)
    {
        string Semitones2Pitch(int semitones)
        {
            int[] steps = { 0, 0, 1, 1, 2, 3, 3, 4, 4, 5, 5, 6 };
            string[] alterations =
            {
                "NATURAL", "SHARP", "NATURAL", "SHARP", "NATURAL", "NATURAL",
                "SHARP", "NATURAL", "SHARP", "NATURAL", "SHARP", "NATURAL",
            };

            int octave = 0;
            while (semitones > 11)
            {
                octave += 1;
                semitones -= 12;
            }

            while (semitones < 0)
            {
                octave -= 1;
                semitones += 12;
            }

            return PythonRegex.Format(
                "%d %d %s", octave, steps[semitones], alterations[semitones]);
        }

        string ConvertTones(string semitoneList)
        {
            string[] tones = semitoneList.Split(
                (char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            string res = string.Empty;
            foreach (string tone in tones)
            {
                res += ",(ly:make-pitch " + Semitones2Pitch(PythonRegex.ToInt(tone)) + ") ";
            }

            return res;
        }

        string NewTunings(Match matchobj)
            => "stringTunings = #`(" + ConvertTones(matchobj.Groups[1].Value) + ")";

        s = PythonRegex.Sub("stringTunings\\s*=\\s*#'\\(([\\d\\s-]*)\\)", NewTunings, s);

        s = PythonRegex.Sub(
            "ukulele-(tenor|baritone)-tuning", "\\1-ukulele-tuning", s);

        if (PythonRegex.Search("[^-]page-top-space", s).Success)
        {
            StdErr(PythonRegex.Format(NotSmart, "page-top-space"));
            StdErr(UpdateManually);
        }

        if (PythonRegex.Search("[^-]between-system-(space|padding)", s).Success)
        {
            StdErr(PythonRegex.Format(NotSmart, "between-system-space, -padding"));
            StdErr(UpdateManually);
        }

        if (PythonRegex.Search("[^-](before|between|after)-title-space", s).Success)
        {
            StdErr(PythonRegex.Format(
                NotSmart, "before-, between-, after-title-space"));
            StdErr(UpdateManually);
        }

        if (PythonRegex.Search("\\\\name\\s", s).Success)
        {
            StdErr("\n"
                + "Vertical spacing changes might affect user-defined contexts." + "\n");
            StdErr(UpdateManually);
        }

        return s;
    }

    //convertrules.py:3253-3261
    private static string Rule_2_13_48(string s)
    {
        string SizeAsExtent(Match matchobj)
        {
            //python's "%g": shortest of %e/%f, six significant digits, trailing zeros
            //removed. C#'s "G6" is the same rule and the same digit count.
            double value = double.Parse(
                matchobj.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture) / 2;
            string half = value.ToString(
                "G6", System.Globalization.CultureInfo.InvariantCulture);
            return "bar-extent = #'(-" + half + " . " + half + ")";
        }

        s = PythonRegex.Sub("bar-size\\s*=\\s*#([0-9\\.]+)", SizeAsExtent, s);
        return s;
    }

    //convertrules.py:3602-3603
    private static string PathReplace(Match m)
        => m.Groups[1].Value
            + string.Join(".", PythonRegex.FindAll(Wordsyntax, m.Groups[2].Value));

    //convertrules.py:3605-3607
    private static string ConvertOverridesToDots(string s)
        => PythonRegex.Sub(
            "(\\\\(?:override|revert)\\s+)(" + GrobSpec + "\\s+" + GrobPath + ")",
            PathReplace, s);

    //convertrules.py:3369-3371
    private static string StripExport(string s)
        => PythonRegex.Sub(
            "\\(ly:export\\s+(" + ParenMatcher(25) + ")\\)", "\\1", s);

    //convertrules.py:3357-3358
    private static string UndollarScm(Match m)
        => PythonRegex.Sub("\\$(.?)", "\\1", m.Value);

    //convertrules.py:3361-3366
    private static string UndollarEmbedded(Match m, string subject)
    {
        string s = PythonRegex.Sub("#\\$", "#", m.Groups[1].Value);

        //Poor man's matched paren scanning after #, gives up after 25 levels.
        s = PythonRegex.Sub("#`?\\(" + ParenMatcher(25) + "\\)", UndollarScm, s);
        return subject.Substring(m.Index, m.Groups[1].Index - m.Index)
            + s
            + subject.Substring(
                m.Groups[1].Index + m.Groups[1].Length,
                (m.Index + m.Length) - (m.Groups[1].Index + m.Groups[1].Length));
    }

    //convertrules.py:3374-3377
    private static string ExportPuller(Match m, string subject)
    {
        if (!PythonRegex.Search("ly:export\\s+", m.Value).Success)
        {
            return m.Value;
        }

        return "$" + StripExport(subject.Substring(m.Index + 1, m.Length - 1));
    }

    //convertrules.py:3380-3381
    private static string UglyFunctionRewriter(Match m, string subject)
    {
        return subject.Substring(m.Index, m.Groups[1].Index - m.Index)
            + StripExport(m.Groups[1].Value)
            + subject.Substring(
                m.Groups[1].Index + m.Groups[1].Length,
                (m.Index + m.Length) - (m.Groups[1].Index + m.Groups[1].Length));
    }

    /// <summary>
    /// convertrules.py's <c>should_really_be_music_function</c>, which
    /// <see cref="RecordUgly"/> APPENDS to as it goes — so it is a variable, not a
    /// constant, and the generator is told to leave it alone.
    /// </summary>
    /// <remarks>
    /// ⚠ Upstream keeps this in a module global, so when <c>convert-ly</c> is given
    /// several files in one run, what it learned from file 1 is still set for file 2.
    /// This port resets it at the start of every conversion
    /// (<see cref="ResetRuleState"/>), which is what running <c>convert-ly</c> once per
    /// file does — and one file per call is the only shape a library API has.
    /// </remarks>
    private static string ShouldReallyBeMusicFunction
        = ShouldReallyBeMusicFunctionDefault;

    private const string ShouldReallyBeMusicFunctionDefault
        = "(?:set-time-signature|empty-music|add-grace-property"
        + "|remove-grace-property|set-accidental-style)";

    /// <summary>Puts the rules' own accumulated state back to its starting value.</summary>
    internal static void ResetRuleState()
        => ShouldReallyBeMusicFunction = ShouldReallyBeMusicFunctionDefault;

    //convertrules.py:3389-3395
    private static string RecordUgly(Match m)
    {
        if (!PythonRegex.MatchAt(ShouldReallyBeMusicFunction, m.Groups[1].Value).Success
            && PythonRegex.Search("ly:export\\s+", m.Groups[2].Value).Success)
        {
            ShouldReallyBeMusicFunction
                = ShouldReallyBeMusicFunction.Substring(
                    0, ShouldReallyBeMusicFunction.Length - 1)
                + "|" + m.Groups[1].Value + ")";
        }

        return m.Value;
    }

    //convertrules.py:3398-3412
    private static string Rule_2_15_18(string s)
    {
        s = PythonRegex.Sub(
            "(?s)#@?\\{(.*?)#@?\\}", UndollarEmbedded, s);
        s = PythonRegex.Sub(
            "#\\(define(?:-public)?\\s+\\(([-a-zA-Z]+)\\b[^()]*?\\)("
            + ParenMatcher(25) + ")\\)",
            RecordUgly, s);
        s = PythonRegex.Sub(
            "\\(define(?:-public)?\\s+\\(" + ShouldReallyBeMusicFunction
            + "\\b[^()]*\\)(" + ParenMatcher(25) + ")\\)",
            UglyFunctionRewriter, s);
        s = PythonRegex.Sub(
            "#(?=\\(" + ShouldReallyBeMusicFunction + ")", "$", s);
        s = PythonRegex.Sub("#\\(markup\\*(?=\\s)", "$(markup", s);
        s = PythonRegex.Sub("#\\(" + ParenMatcher(25) + "\\)", ExportPuller, s);
        if (PythonRegex.Search("\\(ly:export\\s+", s).Success)
        {
            StdErr(PythonRegex.Format(NotSmart, "ly:export"));
        }

        return s;
    }

    //convertrules.py:3467-3490
    private static string Rule_2_15_32(string s)
    {
        string SubTempo(Match m)
        {
            int num = PythonRegex.ToInt(m.Groups[1].Value);
            int den = PythonRegex.ToInt(m.Groups[2].Value);

            if ((den & (den - 1)) != 0) { return m.Value; }

            //Don't try dotted forms if they result in less than 30 bpm. It is not
            //actually relevant to get this right since this only occurs in
            //non-printing situations.
            if (den >= 16 && (num % 7) == 0 && num >= 210)
            {
                return PythonRegex.Format("\\tempo %d.. = %d", den / 4, num / 7);
            }

            if (den >= 8 && (num % 3) == 0 && num >= 90)
            {
                return PythonRegex.Format("\\tempo %d. = %d", den / 2, num / 3);
            }

            return PythonRegex.Format("\\tempo %d = %d", den, num);
        }

        s = PythonRegex.Sub(
            "\\\\context\\s*@?\\{\\s*\\\\Score\\s+tempoWholesPerMinute\\s*=\\s*"
            + "#\\(ly:make-moment\\s+([0-9]+)\\s+([0-9]+)\\)\\s*@?\\}",
            SubTempo, s);
        return s;
    }

    //convertrules.py:3493-3506
    private static string Rule_2_15_39(string s)
    {
        //not_first: the first alternative in the pattern exists only to CONSUME a
        //markup that must not be touched, so a match on it is copied through and only
        //the second alternative expands the template.
        MatchEvaluator NotFirst(string template)
            => m => m.Groups[1].Success
                ? m.Value
                : PythonRegex.Expand(m, template);

        s = PythonRegex.Sub(
            "(" + Matchfullmarkup + ")|"
            + "(\\\\footnote(?:\\s*" + Matchmarkup + ")?" + Matcharg
            + "(?:" + Matcharg + ")?\\s+" + Matchmarkup + ")",
            NotFirst("\\2 \\\\default"), s);
        return s;
    }

    //convertrules.py:3660-3697
    private static string Rule_2_17_11(string s)
    {
        string SubDur(Match m)
        {
            int num = PythonRegex.ToInt(m.Groups[1].Value);
            int den = PythonRegex.ToInt(m.Groups[2].Value);

            //If den is no power of 2, don't even try to use an unscaled duration.
            if ((den & (den - 1)) != 0)
            {
                return PythonRegex.Format("\\tupletSpan 1*%d/%d", num, den);
            }

            if (den >= 4 && num == 7)
            {
                return PythonRegex.Format("\\tupletSpan %d..", den / 4);
            }

            if (den >= 2 && num == 3)
            {
                return PythonRegex.Format("\\tupletSpan %d.", den / 2);
            }

            if (num == 1)
            {
                return PythonRegex.Format("\\tupletSpan %d", den);
            }

            return PythonRegex.Format("\\tupletSpan 1*%d/%d", num, den);
        }

        s = PythonRegex.Sub(
            "\\\\set\\s+tupletSpannerDuration\\s*=\\s*"
            + "#\\(ly:make-moment\\s+([0-9]+)\\s+([0-9]+)\\s*\\)",
            SubDur, s);
        s = PythonRegex.Sub(
            "\\\\unset tupletSpannerDuration", "\\\\tupletSpan \\\\default", s);
        s = PythonRegex.Sub(
            "\\\\times(\\s*)([0-9]+)/([0-9]+)", "\\\\tuplet\\1\\3/\\2", s);

        s = PythonRegex.Sub(
            "(\\(ly:make-moment\\s+-?[0-9]+)\\s+([1-9][0-9]*\\))", "\\1/\\2", s);
        s = PythonRegex.Sub(
            "(\\(ly:make-moment\\s+-?[0-9]+)\\s+([0-9]+\\s+-?[0-9]+)\\s([0-9]+\\))",
            "\\1/\\2/\\3", s);
        s = PythonRegex.Sub(
            "(\\(ly:make-duration\\s+-?[0-9]+\\s+[0-9]+\\s+[0-9]+)\\s+([0-9]+\\))",
            "\\1/\\2", s);
        return s;
    }

    //convertrules.py:3723-3748
    private static string Rule_2_17_15(string s)
    {
        if (PythonRegex.Search("[#$]\\(ly:set-option\\s+'old-relative", s).Success)
        {
            StdErr(PythonRegex.Format(NotSmart, "#(ly:set-option 'old-relative)"));
            StdErr(UpdateManually);
            throw new FatalConversionError();
        }

        //If the file contains a language switch to a language where the name of c is
        //not "c", we can't reliably know which parts of the file will need "c" and
        //which need "do".
        Match m = PythonRegex.Search(
            "\\\\language\\s(?!\\s*#?\"(?:nederlands|deutsch|english|norsk|suomi"
            + "|svenska))\"", s);
        string doPitch;
        if (m.Success)
        {
            //Heuristic: if there is a non-commented { before the language selection,
            //we can't be sure. Also if there is any selection of a non-do language.
            doPitch = PythonRegex.Search(
                    "^[^%\n]*\\{", s.Substring(0, m.Index), PythonRegex.Multiline).Success
                || PythonRegex.Search(
                    "\\\\language\\s(?!\\s*#?\"(?:catalan|espanol|español|italiano"
                    + "|français|portugues|vlaams))\"", s).Success
                ? "$(ly:make-pitch 0 0)"
                : "do'";
        }
        else
        {
            doPitch = "c'";
        }

        s = PythonRegex.Sub(
            "(\\\\relative)(\\s+(\\{|[\\\\<]))", "\\1 " + doPitch + "\\2", s);
        return s;
    }

    //convertrules.py:4113-4120
    private static string Rule_2_19_39(string s)
    {
        s = PythonRegex.Sub(
            "(\\s)((?:markup-markup-spacing|markup-system-spacing"
            + "|score-markup-spacing|last-bottom-spacing"
            + "|score-system-spacing|system-system-spacing"
            + "|top-markup-spacing|top-system-spacing)"
            + "(?:\\s+#\\s*'\\s*" + Wordsyntax + ")+)(?=\\s*=)",
            PathReplace, s);
        return s;
    }

    //convertrules.py:3616-3657
    private static string Rule_2_17_6(string s)
    {
        string Patrep(Match m)
        {
            string FnPathReplace(Match inner)
            {
                string x = string.Join(
                    ".", PythonRegex.FindAll(Wordsyntax, inner.Groups[2].Value));
                switch (x)
                {
                    case "TimeSignature":
                    case "KeySignature":
                    case "BarLine":
                    case "Clef":
                    case "StaffSymbol":
                    case "OttavaBracket":
                    case "LedgerLineSpanner":
                        x = "Staff." + x;
                        break;
                }

                return inner.Groups[1].Value + x;
            }

            if (m.Groups[1].Success) { return m.Value; }

            string result = m.Groups[2].Value + m.Groups[4].Value;

            if (m.Groups[3].Success)
            {
                result += PythonRegex.Sub(
                    "(\\s*)(" + SymbolList + ")", FnPathReplace, m.Groups[3].Value);

                if (!m.Groups[5].Success)
                {
                    result = "\\single" + result;
                }
            }

            return result;
        }

        s = PythonRegex.Sub(
            "(\\\\accidentalStyle\\s+)#?\"([-A-Za-z]+)\"", "\\1\\2", s);
        s = PythonRegex.Sub(
            "(\\\\accidentalStyle\\s+)#'([A-Za-z]+)\\s+#?\"?([-A-Za-z]+)\"?",
            "\\1\\2.\\3", s);
        s = PythonRegex.Sub(
            "(\\\\(?:alterBroken|overrideProperty)\\s+)#?\"([A-Za-z]+)\\s*\\.\\s*"
            + "([A-Za-z]+)\"",
            "\\1\\2.\\3", s);
        s = PythonRegex.Sub(
            "(\\\\tweak\\s+)#?\"?([A-W][A-Za-z]*)\"?\\s+?#'([a-zX-Z][-A-Za-z]*)",
            "\\1\\2.\\3", s);
        s = PythonRegex.Sub(
            "(\\\\tweak\\s+)#'([a-zX-Z][-A-Za-z]*)", "\\1\\2", s);
        s = Footnotec.Replace(s, Patrep);
        s = PythonRegex.Sub(
            "(\\\\alterBroken)(\\s+[A-Za-z.]+)(" + Matcharg + Matcharg + ")",
            "\\1\\3\\2", s);
        s = PythonRegex.Sub(
            "(\\\\overrideProperty\\s+)(" + GrobSpec + "\\s+" + GrobPath + ")",
            PathReplace, s);
        s = ConvertOverridesToDots(s);
        return s;
    }

    //convertrules.py:3700-3720
    private static string Rule_2_17_14(string s)
    {
        string MatchAccepts(Match m)
        {
            //First weed out definitions starting from an existing definition: we
            //assume that the inherited \defaultchild is good enough for our purposes.
            //Heuristic: starts with a backslash and an uppercase letter.
            if (PythonRegex.MatchAt("\\s*\\\\[A-Z]", m.Groups[1].Value).Success)
            {
                return m.Value;
            }

            //Existing defaultchild obviously trumps all.
            if (PythonRegex.Search(
                "\\\\defaultchild[^-_a-zA-Z]", m.Groups[1].Value).Success)
            {
                return m.Value;
            }

            //Take the first \accepts if any and replicate it.
            return PythonRegex.Sub(
                "(\r?\n[ \t]*|[ \t]+)\\\\accepts(\\s+(?:#?\".*?\"|[-_a-zA-Z]+))",
                "\\g<0>\\1\\\\defaultchild\\2",
                m.Value, 1);
        }

        s = PythonRegex.Sub(
            "\\\\context\\s*@?\\{(" + BraceMatcher(20) + ")\\}", MatchAccepts, s);
        return s;
    }

    //convertrules.py:3806-3857
    private static string Rule_2_17_25(string s)
    {
        //This goes for \tempo commands ending with a range, like = 50 ~ 60, and uses -
        //instead. We don't explicitly look for \tempo since the complete syntax has a
        //large number of variants, and this is quite unlikely to occur in other
        //contexts.
        s = PythonRegex.Sub("(=\\s*[0-9]+\\s*)~(\\s*[0-9]+\\s)", "\\1-\\2", s);

        //Match strings, and articulation shorthands that end in -^_ so that we leave
        //alone -| in quoted strings and c4--|.
        string SubNonString(Match m)
            => m.Groups[1].Success ? m.Groups[1].Value + "!" : m.Value;

        s = PythonRegex.Sub(
            "([-^_])\\||" + Matchstring + "|[-^_][-^_]", SubNonString, s);
        s = PythonRegex.Sub("\\bdashBar\\b", "dashBang", s);

        string[] orig =
        {
            "pipeSymbol", "bracketOpenSymbol", "bracketCloseSymbol", "tildeSymbol",
            "parenthesisOpenSymbol", "parenthesisCloseSymbol",
            "escapedExclamationSymbol", "escapedParenthesisOpenSymbol",
            "escapedParenthesisCloseSymbol", "escapedBiggerSymbol",
            "escapedSmallerSymbol",
        };

        string[] repl =
        {
            "\"|\"", "\"[\"", "\"]\"", "\"~\"", "\"(\"", "\")\"",
            "\"\\\\!\"", "\"\\\\(\"", "\"\\\\)\"", "\"\\\\>\"", "\"\\\\<\"",
        };

        string words = "\\b(?:(" + string.Join(")|(", orig) + "))\\b";

        string WordReplace(Match m)
        {
            string InString(Match inner)
                => PythonRegex.Sub(
                    "[\"\\\\]", "\\\\\\g<0>", repl[PythonRegex.LastIndex(inner) - 1]);

            int last = PythonRegex.LastIndex(m);
            return last != 0
                ? repl[last - 1]
                : "\"" + PythonRegex.Sub(
                    words, InString, m.Value.Substring(1, m.Value.Length - 2)) + "\"";
        }

        s = PythonRegex.Sub(words + "|" + Matchstring, WordReplace, s);
        return s;
    }

    //convertrules.py:4123-4139
    private static string Rule_2_19_40(string s)
    {
        string Repl1(Match m)
            => m.Groups[1].Value + PythonRegex.Sub("\\s+", ",", m.Groups[2].Value);

        s = PythonRegex.Sub(
            "(beatStructure\\s*=\\s*)#'\\(([0-9]+(?:\\s+[0-9]+)+)\\)", Repl1, s);

        s = PythonRegex.Sub(
            "(\\\\time\\s*)#'\\(([0-9]+(?:\\s+[0-9]+)+)\\)", Repl1, s);

        string Repl2(Match m)
        {
            string subst = PythonRegex.Sub("\\s+", ",", m.Groups[1].Value);
            return subst
                + new string(' ', 4 + m.Groups[1].Value.Length - subst.Length)
                + m.Groups[2].Value;
        }

        s = PythonRegex.Sub(
            "#'\\(([0-9]+(?:\\s+[0-9]+)+)\\)(\\s+%\\s*beatStructure)", Repl2, s);
        return s;
    }

    //convertrules.py:4200-4211
    private static string ToLyDuration(Match match)
    {
        string dur = match.Groups["dur"].Value;
        string newDur = dur == "breve" || dur == "longa" || dur == "maxima"
            ? "\\" + dur
            : dur;

        //The match may be EMBEDDED in a larger expression, so the text before the
        //opening quote and after the closing one is carried through untouched.
        int start = match.Index;
        return match.Value.Substring(0, match.Groups["startquote"].Index - start)
            + "{" + newDur + match.Groups["dots"].Value + "}"
            + match.Value.Substring(
                match.Groups["endquote"].Index + match.Groups["endquote"].Length - start);
    }

    //convertrules.py:4213-4224
    private static string ToScmDuration(Match match)
    {
        int durLog;
        switch (match.Groups["dur"].Value)
        {
            case "1": durLog = 0; break;
            case "2": durLog = 1; break;
            case "4": durLog = 2; break;
            case "8": durLog = 3; break;
            case "16": durLog = 4; break;
            case "32": durLog = 5; break;
            case "64": durLog = 6; break;
            case "128": durLog = 7; break;
            case "256": durLog = 8; break;
            case "breve": durLog = -1; break;
            case "longa": durLog = -2; break;
            default: durLog = -4; break;
        }

        int dotCount = match.Groups["dots"].Value.Length;
        string replacement = "(ly:make-duration " + durLog + " " + dotCount + ")";
        int start = match.Index;
        return match.Value.Substring(0, match.Groups["startquote"].Index - start)
            + replacement
            + match.Value.Substring(
                match.Groups["endquote"].Index + match.Groups["endquote"].Length - start);
    }

    //convertrules.py:4228-4231
    private static string ConvertStringToDurationForCommand(
        string markupCommand, string s)
    {
        s = PythonRegex.Sub(
            "\\\\" + markupCommand + "\\s*" + StringDurationRe, ToLyDuration, s);
        s = PythonRegex.Sub(
            "#:" + markupCommand + "\\s+" + StringDurationRe, ToScmDuration, s);
        return s;
    }

    //convertrules.py:3976-4069
    private static string Rule_2_19_22(string s)
    {
        //whiteout -> whiteout-box
        s = PythonRegex.Sub("\\\\whiteout(?![a-z_-])", "\\\\whiteout-box", s);
        s = PythonRegex.Sub("\\b\\.whiteout(?![a-z_-])\\b", ".whiteout-box", s);
        s = PythonRegex.Sub("#'whiteout(?![a-z_-])\\b", "#'whiteout-box", s);
        s = PythonRegex.Sub("\\bstencil-whiteout\\b", "stencil-whiteout-box", s);

        //(define-xxx-function (parser location ...) -> (define-xxx-function (...)
        string TopSubst(string text)
        {
            string Subst(Match m)
            {
                string SubSub(Match inner)
                    => inner.Groups[1].Value
                        + PythonRegex.Sub(
                            "(?<=\\s|[\"\\\\()])" + inner.Groups[2].Value
                            + "(?=\\s|[\"\\\\()])",
                            "(*location*)",
                            PythonRegex.Sub(
                                "(?<=\\s|[\"\\\\()])parser(?=\\s|[\"\\\\()])",
                                "(*parser*)",
                                TopSubst(inner.Groups[3].Value)));

                return PythonRegex.Sub(
                    "(\\([-a-z]+\\s*\\(+)parser\\s+([-a-z]+)\\s*((?:.|\n)*)$",
                    SubSub, m.Value);
            }

            return PythonRegex.Sub(
                "\\(define-(?:music|event|scheme|void)-function(?=\\s|[\"(])"
                + ParenMatcher(20) + "\\)",
                Subst, text);
        }

        s = TopSubst(s);

        //(xxx ... parser ...) -> (xxx ... ...)
        string Inner(string text)
        {
            string Repl(Match m) => m.Groups[1].Value + Inner(m.Groups[2].Value);

            return PythonRegex.Sub(
                "(\\((?:"
                + "ly:parser-lexer|ly:parser-clone|ly:parser-output-name|"
                + "ly:parser-error|ly:parser-define!|ly:parser-lookup|"
                + "ly:parser-has-error\\?|ly:parser-clear-error|"
                + "ly:parser-set-note-names|ly:parser-include-string|"
                + "note-names-language|display-lily-music|music->lily-string|"
                + "note-name->lily-string|value->lily-string|check-grob-path|"
                + "event-chord-wrap!|collect-bookpart-for-book|"
                + "collect-scores-for-book|collect-music-aux|"
                + "collect-book-music-for-book|scorify-music|"
                + "collect-music-for-book|collect-book-music-for-book|"
                + "toplevel-book-handler|default-toplevel-book-handler|"
                + "print-book-with-defaults|toplevel-music-handler|"
                + "toplevel-score-handler|toplevel-text-handler|"
                + "toplevel-bookpart-handler|book-music-handler|"
                + "context-mod-music-handler|bookpart-music-handler|"
                + "output-def-music-handler|print-book-with-defaults-as-systems|"
                + "add-score|add-text|add-music|make-part-combine-music|"
                + "make-directed-part-combine-music|add-quotable|paper-variable|"
                + "make-autochange-music|context-mod-from-music|"
                + "context-defs-from-music)"
                + "(?=\\s|[()]))(" + ParenMatcher(20) + ")"
                + "(?:\\s+parser(?=\\s|[()])|\\s*\\(\\*parser\\*\\))",
                Repl, text);
        }

        s = Inner(s);

        //This is the simplest case, resulting from one music function trying to call
        //another one via Scheme. The caller is supposed to have its uses of
        //parser/location converted to (*parser*)/(*location*) already. Other uses of
        //ly:music-function-extract are harder to convert but unlikely.
        s = PythonRegex.Sub(
            "(\\(\\s*\\(ly:music-function-extract\\s+" + Wordsyntax
            + "\\s*\\)\\s+)\\(\\*parser\\*\\)\\s*\\(\\*location\\*\\)",
            "\\1", s);

        s = PythonRegex.Sub("ChordNameVoice", "ChordNames", s);
        return s;
    }

    //convertrules.py:4142-4151
    private static string Rule_2_19_46(string s)
    {
        string word = "(?:#?\"[^\"]*\"|\\b" + Wordsyntax + "\\b)";
        List<string> found = PythonRegex.FindAll(
            "\n(" + Wordsyntax + ")\\s*=\\s*\\\\with(?:\\s|\\\\|\\{)", s);
        found.Add("RemoveEmptyStaves");
        found.Add("RemoveAllEmptyStaves");
        string mods = string.Join("|", found);

        s = PythonRegex.Sub(
            "(\\\\(?:drums|figures|chords|lyrics|addlyrics|"
            + "(?:new|context)\\s*" + word
            + "(?:\\s*=\\s*" + word + ")?)\\s*)(\\\\(?:" + mods + "))",
            "\\1\\\\with \\2", s);
        return s;
    }

    //convertrules.py:4183-4188
    private static string Rule_2_20_0(string s)
    {
        List<string> changes = PythonRegex.FindAll(
            "\\\\language\\s*#?\"([a-zçñ]+)\"", s);
        int deutsch = 0;
        foreach (string change in changes)
        {
            if (change == "deutsch") { deutsch++; }
        }

        if (changes.Count > 0 && deutsch == changes.Count)
        {
            s = PythonRegex.Sub("\\bbeh\\b", "heh", s);
        }

        return s;
    }

    //convertrules.py:4233-4263
    private static string Rule_2_21_0(string s)
    {
        s = ConvertStringToDurationForCommand("note", s);
        s = PythonRegex.Sub(
            "\\(tuplet-number::(?:fraction-with-notes"
            + "|non-default-fraction-with-notes|append-note-wrapper)\\s"
            + ParenMatcher(20) + "\\)",
            match => PythonRegex.Sub(StringDurationRe, ToScmDuration, match.Value),
            s);
        s = PythonRegex.Sub(
            "(\\\\(?:fret-diagram(?:-terse)?|harp-pedal|justify-string"
            + "|lookup|musicglyph|postscript|simple|tied-lyric|verbatim-file"
            + "|with-url|wordwrap-string"
            + "|discant|freeBass|stdBass|stdBassIV|stdBassV|stdBassVI"
            + ")\\s*)[#$](\\\\?\")",
            "\\1\\2", s);
        s = PythonRegex.Sub(
            "\\\\partcombine(Force|Up|Down|Chords|Apart|Unisono|SoloI|SoloII"
            + "|Automatic|)\\b",
            "\\\\partCombine\\1", s);
        s = PythonRegex.Sub("\\\\autochange", "\\\\autoChange", s);
        s = PythonRegex.Sub("\\\\powerChords", "", s);
        s = PythonRegex.Sub(
            "\"scripts\\.trilelement\"", "\"scripts.trillelement\"", s);
        s = PythonRegex.Sub("\\\\fermataMarkup", "\\\\fermata", s);
        s = PythonRegex.Sub(
            "\\\\(compress|expand)FullBarRests", "\\\\\\1EmptyMeasures", s);
        if (PythonRegex.Search("#(banter|jazz)-chordnames", s).Success)
        {
            StdErr(PythonRegex.Format(
                NotSmart, "alternative chord naming functions"));
            StdErr(UpdateManually);
        }

        return s;
    }

    //convertrules.py:4287-4310
    private static string Rule_2_23_1(string s)
    {
        s = PythonRegex.Sub(
            "\"noteheads\\.[ud](1|2)(triangle|(?:do|re|ti)(?:Thin)?)\"",
            "\"noteheads.s\\1\\2\"", s);
        s = PythonRegex.Sub("\\\\bar(\\s+)\"S\"", "\\\\bar\\1\"S-||\"", s);
        s = PythonRegex.Sub("\\\\bar(\\s+)\"S-\\|\"", "\\\\bar\\1\"S\"", s);
        s = PythonRegex.Sub(
            "segnoType(\\s+=\\s+)#?\"S\"", "segnoType\\1\"S-||\"", s);
        s = PythonRegex.Sub(
            "segnoType(\\s+=\\s+)#?\"S-\\|\"", "segnoType\\1\"S\"", s);
        s = ConvertStringToDurationForCommand("rest", s);

        //Be more general than \override #'(multi-measure-rest . #t), there's also
        //\override #'((something . else) (multi-measure-rest . #t)).
        if (s.Contains("#'(multi-measure-rest . #t)") && s.Contains("\\rest-by-number"))
        {
            //Don't convert blindly since it may also be use of \rest-by-number for a
            //normal rest and \rest with \override #'(multi-measure-rest . #t)
            //somewhere else.
            StdErr(PythonRegex.Format(
                NotSmart, "\\override #'(multi-measure-rest . #t) \\rest-by-number"));
            StdErr(MultiMeasureRestWarning);
            StdErr(UpdateManually);
        }

        return s;
    }

    //convertrules.py:4407-4419
    private static readonly (string Pattern, string Replacement)[] OnTheFlyReplacements =
    {
        ("[\\\\#$]print-page-number-check-first", "\\\\if \\\\should-print-page-number"),
        ("[\\\\#$]create-page-number-stencil",
            "\\\\if \\\\should-print-page-numbers-global"),
        ("[\\\\#$]print-all-headers", "\\\\if \\\\should-print-all-headers"),
        ("[\\\\#$]first-page", "\\\\if \\\\on-first-page"),
        ("[\\\\#$]not-first-page", "\\\\unless \\\\on-first-page"),
        ("[#$]\\(on-page (\\d+)\\)", "\\\\if \\\\on-page #\\1"),
        ("[\\\\#$]last-page", "\\\\if \\\\on-last-page"),
        ("[\\\\#$]part-first-page", "\\\\if \\\\on-first-page-of-part"),
        ("[\\\\#$]not-part-first-page", "\\\\unless \\\\on-first-page-of-part"),
        ("[\\\\#$]part-last-page", "\\\\if \\\\on-last-page-of-part"),
        ("[\\\\#$]not-single-page", "\\\\unless \\\\single-page"),
    };

    //convertrules.py:4460
    private static readonly string[] DashAbbreviations =
        { "Hat", "Plus", "Dash", "Bang", "Larger", "Dot", "Underscore" };

    //convertrules.py:4333-4365
    private static string Rule_2_23_2(string s)
    {
        //Detect changes to the Stem.neutral-direction property in conjunction with the
        //Melody_engraver. Convert
        //  \consists Melody_engraver
        //  \override Stem.neutral-direction = #'()
        //to just the \consists, and warn about other uses.
        const string neutralDir = "Stem\\.neutral-direction";
        string neutralDirOverride = "\\\\override\\s+" + neutralDir + "\\s+=\\s+#'\\(\\)";
        const string melodyEngraver = "\\\\consists\\s+\"?Melody_engraver\"?";
        string typicalUsage = "(" + melodyEngraver + ")\\s+" + neutralDirOverride;
        s = PythonRegex.Sub(typicalUsage, "\\1", s);
        if (PythonRegex.Search(neutralDir, s).Success
            && PythonRegex.Search("Melody_engraver", s).Success)
        {
            StdErr(PythonRegex.Format(
                NotSmart, "Stem.neutral-direction with Melody_engraver"));
            StdErr(MelodyEngraverWarning);
            StdErr(UpdateManually);
        }

        s = PythonRegex.Sub(
            "\\(scm (accreg|display-lily|graphviz|guile-debugger|song|to-xml)\\)",
            "(lily \\1)", s);
        return s;
    }

    //convertrules.py:4421-4442
    private static string Rule_2_23_4(string s)
    {
        s = PythonRegex.Sub("ly:context-now", "ly:context-current-moment", s);

        //It's unlikely that users would have wanted different settings for the item
        //type and the spanner type, so this should be reasonable.
        const string itemSpanner
            = "(ControlPoint|ControlPolygon|Footnote|BalloonText)(Item|Spanner)";
        s = PythonRegex.Sub(itemSpanner, "\\1", s);
        s = PythonRegex.Sub("ParenthesesItem", "Parentheses", s);
        s = PythonRegex.Sub("parentheses-item::", "parentheses-interface::", s);
        s = PythonRegex.Sub(TrillPitchGroupRe, Repl, s, PythonRegex.Verbose);
        foreach ((string pattern, string replacement) in OnTheFlyReplacements)
        {
            s = PythonRegex.Sub("\\\\on-the-fly\\s+" + pattern, replacement, s);
        }

        return s;
    }

    //convertrules.py:4475-4516
    private static string Rule_2_23_6(string s)
    {
        s = PythonRegex.Sub("defaultBarType", "measureBarType", s);
        s = PythonRegex.Sub("doubleRepeatSegnoType", "doubleRepeatSegnoBarType", s);
        s = PythonRegex.Sub("doubleRepeatType", "doubleRepeatBarType", s);
        s = PythonRegex.Sub("endRepeatSegnoType", "endRepeatSegnoBarType", s);
        s = PythonRegex.Sub("endRepeatType", "endRepeatBarType", s);
        s = PythonRegex.Sub("fineSegnoType", "fineSegnoBarType", s);
        s = PythonRegex.Sub(
            "fineStartRepeatSegnoType", "fineStartRepeatSegnoBarType", s);
        s = PythonRegex.Sub("markFormatter", "rehearsalMarkFormatter", s);
        s = PythonRegex.Sub("segnoType", "segnoBarType", s);
        s = PythonRegex.Sub("startRepeatSegnoType", "startRepeatSegnoBarType", s);
        s = PythonRegex.Sub("startRepeatType", "startRepeatBarType", s);
        s = PythonRegex.Sub("underlyingRepeatType", "underlyingRepeatBarType", s);
        s = PythonRegex.Sub(
            "((make-articulation|'articulation-type)\\s+)\"(\\w+)\"", "\\1'\\3", s);
        s = PythonRegex.Sub(
            PythonRegex.Format(
                "(dash(%s)\\s+)=(\\s+)\"(\\w+)\"",
                string.Join("|", DashAbbreviations)),
            "\\1=\\3#(make-articulation '\\4)", s);

        //The case (markup->string <symbol>) is easy to detect and should not be warned
        //about. Cases with one argument that is more complex than a symbol are harder
        //to detect reliably, so we conservatively print the warning.
        if (PythonRegex.Search(
            "(?!(?<=\\()markup\\->string\\s+\\w+\\))markup->string", s).Success)
        {
            StdErr(PythonRegex.Format(NotSmart, "markup->string"));
            StdErr(Markup2stringWarning);
            StdErr(UpdateManually);
        }

        s = s.Replace(
            "ly:grob-spanned-rank-interval", "ly:grob-spanned-column-rank-interval");
        return s;
    }

    //convertrules.py:4640-4657
    private static string Rule_2_23_11(string s)
    {
        //We convert only \bar because automatic bar lines configured with context
        //properties layer themselves.
        string[] changedBarTypes =
        {
            "\\.\\|",       // .|
            "\\.\\|:",      // .|:
            "\\[\\|:",      // [|:
            "S",            // S
            "S\\.\\|:",     // S.|:
        };

        s = PythonRegex.Sub(
            "\\\\bar\\s*\"(" + string.Join("|", changedBarTypes) + ")\"",
            "\\\\bar \"\\1-|\"", s);

        //New syntax was introduced in 2.17.6, but this version adds a warning, which
        //is more insistent.
        s = ConvertOverridesToDots(s);
        return s;
    }

    //convertrules.py:4819-4833
    private static string MakeMomentToMusicLength(Match match)
    {
        int n = PythonRegex.ToInt(match.Groups["numerator"].Value);
        int d = PythonRegex.ToInt(match.Groups["denominator"].Value);

        //It's tempting to use musicexp.Duration, but we want this frozen in time. We
        //also avoid reducing fractions; for example, we turn `4/4` into `4*4` rather
        //than `1`.
        int dlog = BitLength(d) - 1;
        if ((1 << dlog) == d)
        {
            //d is a power of 2
            if (n == 1) { return "\\musicLength " + d; }

            if (n == 3) { return "\\musicLength " + (1 << (dlog - 1)) + "."; }

            return "\\musicLength " + d + "*" + n;
        }

        return "\\musicLength 1*" + n + "/" + d;
    }

    /// <summary>python's <c>int.bit_length</c>.</summary>
    /// <param name="value">The number.</param>
    /// <returns>How many bits it needs.</returns>
    private static int BitLength(int value)
    {
        int bits = 0;
        for (int v = value < 0 ? -value : value; v != 0; v >>= 1) { bits++; }

        return bits;
    }

    //convertrules.py:4835-4845
    private static string Rule_2_25_3(string s)
    {
        //#(ly:make-moment num den) or #(ly:make-moment num/den)
        s = PythonRegex.Sub(MakeMomentRe, MakeMomentToMusicLength, s);

        //#(ly:make-moment n)
        s = PythonRegex.Sub(
            "#\\(\\s*ly:make-moment\\s+(\\d+)\\s*\\)", "\\\\musicLength 1*\\1", s);

        //Clean up 1*1 from previous rule.
        s = PythonRegex.Sub(
            "\\\\musicLength 1\\*1(?!\\S)", "\\\\musicLength 1", s);
        return s;
    }

    //convertrules.py:5350-5362
    private static string MakeMomAssignRe(string oldPropertyName)
        => "\\b" + oldPropertyName
            + "(?P<assignment>\\s*=\\s*)"
            + "("
            + "\\\\musicLength\\s+"
            + "(?P<head>\\d+)"
            + "(?P<dots>\\.*)"
            + "(\\*(?P<factor_num>\\d+)(/(?P<factor_den>\\d+))?)?"
            + "|"
            + "#(?P<special_value>(INF-MOMENT|ZERO-MOMENT))"
            + ")";

    //convertrules.py:5364-5389
    private static MatchEvaluator MakeMomAssignReplacer(string newPropertyName)
        => match =>
        {
            string eq = match.Groups["assignment"].Value;
            if (match.Groups["special_value"].Value == "INF-MOMENT")
            {
                return newPropertyName + eq + "#+inf.0";
            }

            if (match.Groups["special_value"].Value == "ZERO-MOMENT")
            {
                return newPropertyName + eq + "0";
            }

            long head = PythonRegex.ToInt(match.Groups["head"].Value);
            int dots = match.Groups["dots"].Value.Length;
            long num = match.Groups["factor_num"].Success
                ? PythonRegex.ToInt(match.Groups["factor_num"].Value)
                : 1;
            long den = match.Groups["factor_den"].Success
                ? PythonRegex.ToInt(match.Groups["factor_den"].Value)
                : 1;

            if (dots == 0 && den == 1)
            {
                if (head == 1) { return newPropertyName + eq + num; }

                //There might be some explanatory value in fractions that are not
                //reduced, so for example, we turn `8*4` into `#4/8` rather than `#1/2`.
                return newPropertyName + eq + "#" + num + "/" + head;
            }

            //We're losing the basic duration (h) anyway, so reduce it.
            (long fn, long fd) = Reduce(num, head * den);
            if (dots != 0)
            {
                (fn, fd) = Reduce(
                    fn * ((1L << (dots + 1)) - 1), fd * (1L << dots));
            }

            return fd == 1
                ? newPropertyName + eq + fn
                : newPropertyName + eq + "#" + fn + "/" + fd;
        };

    /// <summary>Reduces a fraction, as python's <c>Fraction</c> does on construction.</summary>
    /// <param name="numerator">The numerator.</param>
    /// <param name="denominator">The denominator.</param>
    /// <returns>The reduced fraction.</returns>
    private static (long Numerator, long Denominator) Reduce(
        long numerator, long denominator)
    {
        long a = numerator < 0 ? -numerator : numerator;
        long b = denominator < 0 ? -denominator : denominator;
        while (b != 0) { (a, b) = (b, a % b); }

        long divisor = a == 0 ? 1 : a;
        return (numerator / divisor, denominator / divisor);
    }

    //convertrules.py:5391-5407
    private static string Rule_2_25_22(string s)
    {
        s = PythonRegex.Sub("(\\(\\s*)base-length(\\s)", "\\1beat-base\\2", s);
        s = PythonRegex.Sub(
            MakeMomAssignRe("baseMoment"), MakeMomAssignReplacer("beatBase"), s);

        //This isn't necessary, but add `#` in this case for consistency with the
        //beatBase = ... requirement.
        s = PythonRegex.Sub(
            "(\\\\overrideTimeSignatureSettings\\s+\\d+/\\d+\\s+)(\\d+/\\d+)\\b",
            "\\1#\\2", s);
        s = PythonRegex.Sub(
            "(\\(\\s*)duration-length(\\s)", "\\1ly:duration->number\\2", s);
        s = PythonRegex.Sub(
            "(\\(\\s*)ly:duration-length(\\s)", "\\1ly:duration->moment\\2", s);
        return s;
    }

    //convertrules.py:5410-5465
    private static string Rule_2_25_23(string s)
    {
        s = PythonRegex.Sub(
            MakeMomAssignRe("maximumBeamSubdivisionInterval"),
            MakeMomAssignReplacer("beamMaximumSubdivision"), s);
        s = PythonRegex.Sub(
            "(\\\\unset\\s+)maximumBeamSubdivisionInterval",
            "\\1beamMaximumSubdivision", s);
        s = PythonRegex.Sub(
            MakeMomAssignRe("minimumBeamSubdivisionInterval"),
            MakeMomAssignReplacer("beamMinimumSubdivision"), s);
        s = PythonRegex.Sub(
            "(\\\\unset\\s+)minimumBeamSubdivisionInterval",
            "\\1beamMinimumSubdivision", s);
        s = PythonRegex.Sub(
            "\\btempoWholesPerMinute\\b", "tempoWholesPerMinuteAsMoment", s);
        s = PythonRegex.Sub(
            MakeMomAssignRe("tempoWholesPerMinuteAsMoment"),
            MakeMomAssignReplacer("tempoWholesPerMinute"), s);
        s = PythonRegex.Sub(
            "\\bvoltaSpannerDuration\\b", "voltaSpannerDurationAsMoment", s);
        s = PythonRegex.Sub(
            MakeMomAssignRe("voltaSpannerDurationAsMoment"),
            MakeMomAssignReplacer("voltaSpannerDuration"), s);
        s = PythonRegex.Sub(
            MakeMomAssignRe("minimumPageTurnLength"),
            MakeMomAssignReplacer("pageTurnMinimumRestLength"), s);
        s = PythonRegex.Sub(
            MakeMomAssignRe("minimumRepeatLengthForPageTurn"),
            MakeMomAssignReplacer("pageTurnMinimumRepeatLength"), s);
        s = PythonRegex.Sub("\\bcompletionUnit\\b", "completionUnitAsMoment", s);
        s = PythonRegex.Sub(
            MakeMomAssignRe("completionUnitAsMoment"),
            MakeMomAssignReplacer("completionUnit"), s);
        s = PythonRegex.Sub("\\bgridInterval\\b", "gridIntervalAsMoment", s);
        s = PythonRegex.Sub(
            MakeMomAssignRe("gridIntervalAsMoment"),
            MakeMomAssignReplacer("gridInterval"), s);
        s = PythonRegex.Sub(
            "\\bproportionalNotationDuration\\b",
            "proportionalNotationDurationAsMoment", s);
        s = PythonRegex.Sub(
            MakeMomAssignRe("proportionalNotationDurationAsMoment"),
            MakeMomAssignReplacer("proportionalNotationDuration"), s);
        s = PythonRegex.Sub(
            "\\btupletSpannerDuration\\b", "tupletSpannerDurationAsMoment", s);
        s = PythonRegex.Sub(
            MakeMomAssignRe("tupletSpannerDurationAsMoment"),
            MakeMomAssignReplacer("tupletSpannerDuration"), s);
        s = PythonRegex.Sub(
            "(\\(\\s*)calculate-compound-base-beat(\\s)",
            "\\1calculate-compound-beat-base-as-moment\\2", s);
        s = PythonRegex.Sub(
            "(\\(\\s*)calculate-compound-measure-length(\\s)",
            "\\1calculate-compound-measure-length-as-moment\\2", s);
        s = PythonRegex.Sub("\\bmeasureLength\\b", "measureLengthAsMoment", s);
        s = PythonRegex.Sub(
            MakeMomAssignRe("measureLengthAsMoment"),
            MakeMomAssignReplacer("measureLength"), s);
        return s;
    }

    //convertrules.py:4863-5118. The font-selection rule: it rewrites a
    //(set-global-fonts ...) or (make-pango-font-tree ...) call into `fonts.<family> ='
    //lines, warns about the parts it cannot carry over, and then does a run of plain
    //renames.
    private static string Rule_2_25_4(string s)
    {
        //This replacement is not 100% reliable, but should do a good job. If there is
        //the #:factor (/ staff-height pt 20) parameter in the set-global-fonts call,
        //the user had done what it took to heed any custom font size, so we're fine.
        //If no #:factor was passed, we assume that the user was not changing the font
        //size, as doing that without passing #:factor was resulting in bad output
        //anyway. Other values result in a warning.
        const string setGlobalFontsFactorWarning =
            "\nLilyPond now uses a different syntax for selecting fonts.  The\n"
            + "set-global-fonts Scheme function has been removed.  convert-ly\n"
            + "has mostly converted the call for you, but it was not able to\n"
            + "convert the #:factor parameter.  Now, font selection is independent\n"
            + "from setting the font size.  Please use set-global-staff-size\n"
            + "or layout-set-staff-size to replace this parameter, which you\n"
            + "set to:\n\n    {0}\n\n";

        const string setGlobalFontsBraceWarning =
            "\nLilyPond now uses a different syntax for selecting fonts.  The #:brace\n"
            + "argument to set-global-fonts does not have an equivalent in the new "
            + "syntax;\nbraces now use the normal music font family by default.  Your "
            + "code contains\na call to set-global-fonts where the #:music and #:brace "
            + "parameters have\ndifferent values.  If you wish to set braces in a "
            + "different music font than\nother music glyphs, use code such as\n\n"
            + "\\paper {\n  fonts.music-alt = \"your-music-font\"\n}\n\n"
            + "\\layout {\n  \\context {\n    \\Score\n"
            + "    \\override SystemStartBrace.font-family = #'music-alt\n  }\n}\n";

        const string setGlobalFontsAdvancedWarning =
            "\nLilyPond now uses a different syntax for selecting fonts. The code "
            + "equivalent\nto the previous spelling\n\n"
            + "\\paper {\n  #(define fonts\n     (set-global-fonts #:music "
            + "\"music-font\"\n                       #:brace \"brace-font\"\n"
            + "                       #:roman \"roman font\"\n"
            + "                       #:sans \"sans font\"\n"
            + "                       #:typewriter \"typewriter font\"))\n}\n\n"
            + "is now\n\n\\paper {\n  fonts.music = \"music-font\"\n"
            + "  fonts.roman = \"roman font\"\n  fonts.sans = \"sans font\"\n"
            + "  fonts.typewriter = \"typewriter font\"\n}\n\n"
            + "convert-ly found an advanced use of set-global-fonts that it was not "
            + "able to\nconvert automatically.  Please do the update manually.\n";

        const string simpleSexprRe = "(\"[^\"]+\"|[\\w/-]+)";
        string maybeFuncallSexprRe =
            "(\\((" + BraceMatcherParen(10) + ")\\)" + "|" + simpleSexprRe + ")";
        string keyvalRe =
            "#:(?P<key>\\w+)\\s+(?P<val>" + maybeFuncallSexprRe + ")\\s*";
        string setGlobalFontsRe =
            "(\\(set-global-fonts\\s+(?P<args>(" + keyvalRe + ")*)\\))";
        const string indentRe = "^(?P<indent>[^\\S\n]*)";

        string ReplaceSetGlobalFonts(Match match)
        {
            string indent = match.Groups["indent"].Value;
            string call = match.Groups["args"].Value;
            List<string> lines = new List<string>();

            //An ORDERED map: upstream iterates a python dict, which keeps insertion
            //order, and the order decides the order of the emitted `fonts.x =' lines.
            List<KeyValuePair<string, string>> parameters
                = new List<KeyValuePair<string, string>>();
            foreach (Match keyval in PythonRegex.Compile(keyvalRe).Matches(call))
            {
                string key = keyval.Groups["key"].Value;
                int existing = parameters.FindIndex(
                    e => string.Equals(e.Key, key, System.StringComparison.Ordinal));
                if (existing >= 0)
                {
                    parameters[existing]
                        = new KeyValuePair<string, string>(key, keyval.Groups["val"].Value);
                }
                else
                {
                    parameters.Add(
                        new KeyValuePair<string, string>(key, keyval.Groups["val"].Value));
                }
            }

            string Value(string key)
            {
                int index = parameters.FindIndex(
                    e => string.Equals(e.Key, key, System.StringComparison.Ordinal));
                return index < 0 ? null : parameters[index].Value;
            }

            string factorValue = Value("factor");
            if (factorValue != null)
            {
                string[] fac = factorValue.Split(
                    (char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                if (!SameWords(fac, "(/", "staff-height", "pt", "20)")
                    && !SameWords(fac, "(/", "staff-height", "20", "pt)"))
                {
                    StdErr(PythonRegex.Format(
                        NotSmart, "#:factor parameter to set-global-fonts"));
                    StdErr(setGlobalFontsFactorWarning.Replace("{0}", factorValue));
                    StdErr(UpdateManually);
                }
            }

            if (!string.Equals(
                Value("music"), Value("brace"), System.StringComparison.Ordinal))
            {
                StdErr(PythonRegex.Format(
                    NotSmart,
                    "different music and brace fonts passed to set-global-fonts"));
                StdErr(setGlobalFontsBraceWarning);
                StdErr(UpdateManually);
            }

            foreach (KeyValuePair<string, string> entry in parameters)
            {
                if (entry.Key == "factor" || entry.Key == "brace") { continue; }

                string val = entry.Value;
                if (!val.StartsWith("\"", System.StringComparison.Ordinal))
                {
                    val = "#" + val;
                }

                lines.Add(indent + "fonts." + entry.Key + " = " + val);
            }

            return string.Join("\n", lines);
        }

        string defineRe = indentRe + "#\\(define\\s+fonts\\s+" + setGlobalFontsRe + "\\s*\\)";
        string assignRe = indentRe + "fonts\\s*=\\s*#" + setGlobalFontsRe;
        s = PythonRegex.Sub(defineRe, ReplaceSetGlobalFonts, s, PythonRegex.Multiline);
        s = PythonRegex.Sub(assignRe, ReplaceSetGlobalFonts, s, PythonRegex.Multiline);

        //Warn about remaining uses.
        if (s.Contains("set-global-fonts"))
        {
            StdErr(PythonRegex.Format(NotSmart, "advanced use of set-global-fonts"));
            StdErr(setGlobalFontsAdvancedWarning);
            StdErr(UpdateManually);
        }

        //Similar logic here with the factor parameter here.
        const string pangoWarning =
            "\nLilyPond now uses a different syntax for selecting fonts.  The\n"
            + "make-pango-font-tree Scheme function has been removed.  convert-ly\n"
            + "has mostly converted the call for you, but it was not able to\n"
            + "convert the #:factor parameter.  Now, font selection is independent\n"
            + "from setting the font size.  Please use set-global-staff-size\n"
            + "or layout-set-staff-size to replace this parameter, which you\n"
            + "set to:\n\n    {0}\n\n";

        const string pangoAdvancedWarning =
            "\nLilyPond now uses a different syntax for selecting fonts. The code "
            + "equivalent\nto the previous spelling\n\n"
            + "\\paper {\n  #(define fonts\n     (make-pango-font-tree\n"
            + "       \"roman font\"\n       \"sans font\"\n"
            + "       \"typewriter font\"\n       factor))\n}\n\n"
            + "is now\n\n\\paper {\n  fonts.roman = \"roman font\"\n"
            + "  fonts.sans = \"sans font\"\n  fonts.typewriter = \"typewriter font\"\n}\n\n"
            + "convert-ly found an advanced use of make-pango-font-tree that it was "
            + "not able\nto convert automatically.  Please do the update manually.\n";

        string pangoRe =
            "(?P<pango>\\(make-pango-font-tree\\s+"
            + "(?P<roman>" + maybeFuncallSexprRe + ")\\s+"
            + "(?P<sans>" + maybeFuncallSexprRe + ")\\s+"
            + "(?P<typewriter>" + maybeFuncallSexprRe + ")\\s+"
            + "(?P<factor>" + maybeFuncallSexprRe + ")\\s*"
            + "\\))";

        string ReplacePango(Match match)
        {
            string indent = match.Groups["indent"].Value;
            List<string> lines = new List<string>();
            foreach (string family in new[] { "roman", "sans", "typewriter" })
            {
                string font = match.Groups[family].Value;
                if (!font.StartsWith("\"", System.StringComparison.Ordinal))
                {
                    font = "#" + font;
                }

                lines.Add(indent + "fonts." + family + " = " + font);
            }

            string[] factor = match.Groups["factor"].Value.Split(
                (char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            if (!SameWords(factor, "1")
                && !SameWords(factor, "(/", "staff-height", "pt", "20)")
                && !SameWords(factor, "(/", "staff-height", "20", "pt)"))
            {
                StdErr(PythonRegex.Format(
                    NotSmart, "factor parameter to make-pango-font-tree"));
                StdErr(pangoWarning.Replace("{0}", match.Groups["factor"].Value));
                StdErr(UpdateManually);
            }

            return string.Join("\n", lines);
        }

        string pangoDefineRe = indentRe + "#\\(define\\s+fonts\\s+" + pangoRe + "\\s*\\)";
        string pangoAssignRe = indentRe + "fonts\\s*=\\s*#" + pangoRe;
        s = PythonRegex.Sub(pangoDefineRe, ReplacePango, s, PythonRegex.Multiline);
        s = PythonRegex.Sub(pangoAssignRe, ReplacePango, s, PythonRegex.Multiline);

        if (s.Contains("make-pango-font-tree"))
        {
            StdErr(PythonRegex.Format(
                NotSmart, "advanced use of make-pango-font-tree"));
            StdErr(pangoAdvancedWarning);
            StdErr(UpdateManually);
        }

        //Convert \lookup to \musicglyph if the argument is not a brace glyph. Also
        //strip outer \override #'(font-encoding . fetaMusic) if found; this is not
        //necessary, but it shortens the input code. We detect
        //\override #'(font-encoding . fetaBraces) { \lookup ... } as well because many
        //users put redundant braces in markup.
        const string templateHead = "(?x)\n       ";
        const string templateBody =
            "\n       \\\\lookup  \\s*  \\#?  \" (?!brace)([^\"]+) \"\n       ";
        const string delim1NoBrace =
            "\n      \\\\override  \\s*  \\#'\\(font-encoding \\s+ \\.  \\s+ "
            + "fetaMusic\\)\\s*\n                      ";
        string delim1Brace = delim1NoBrace + "\\{\\s*";
        const string delim2Brace = "\\s*\\}";
        const string lookupRepl = "\\\\musicglyph \"\\1\"";

        s = PythonRegex.Sub(
            templateHead + delim1NoBrace + templateBody + "", lookupRepl, s);
        s = PythonRegex.Sub(
            templateHead + delim1Brace + templateBody + delim2Brace, lookupRepl, s);
        s = PythonRegex.Sub(templateHead + "" + templateBody + "", lookupRepl, s);

        //Convert make-lookup-markup uses.
        s = PythonRegex.Sub(
            "\\(make-lookup-markup\\s+\"(?!brace)([^\"]+)\"\\)",
            "(make-musicglyph-markup \"\\1\")", s);

        //Convert (markup #:lookup ...) uses.
        s = PythonRegex.Sub(
            "#:lookup\\s+\"(?!brace)([^\"]+)\"", "#:musicglyph \"\\1\"", s);

        //For \override
        s = PythonRegex.Sub(
            "font-shape\\s*=\\s*#'caps", "font-variant = #'small-caps", s);

        //For \tweak
        s = PythonRegex.Sub("font-shape\\s+#'caps", "font-variant #'small-caps", s);

        //For \markup \override
        s = PythonRegex.Sub(
            "\\(font-shape\\s+\\.\\s+caps\\)", "(font-variant . small-caps)", s);

        //Convert \medium markup command to \markup \normal-weight
        s = PythonRegex.Sub("\\\\medium", "\\\\normal-weight", s);
        s = PythonRegex.Sub("make-medium-markup", "make-normal-weight-markup", s);
        s = PythonRegex.Sub("#:medium", "#:normal-weight", s);

        //For \override
        s = PythonRegex.Sub(
            "font-series\\s*=\\s*#'medium", "font-series = #'normal", s);

        //For \tweak
        s = PythonRegex.Sub("font-series\\s+#'medium", "font-series #'normal", s);

        //For \markup \override
        s = PythonRegex.Sub(
            "\\(font-series\\s+\\.\\s+medium\\)", "(font-series . normal)", s);

        if (s.Contains("repeatCommands")
            && (s.Contains("(volta \"") || s.Contains("(volta ,#{")))
        {
            StdErr(PythonRegex.Format(NotSmart, "markup in repeatCommands"));
            StdErr("\nMarkups inside repeatCommands are no longer automatically "
                + "typeset in\na music font. Use the \\volta-number command where "
                + "needed. Example\nreplacements:\n\n"
                + "  \\set Score.repeatCommands = #'((volta \"ad lib.\"))\n"
                + "  [does not need conversion]\n\n"
                + "  \\set Score.repeatCommands = #'((volta \"1.2.\"))\n"
                + "  -> \\set Score.repeatCommands = #`((volta ,#{ \\markup "
                + "\\volta-number \"1.2.\" #}))\n\n"
                + "  \\set Score.repeatCommands = #'((volta \"1.2. ad lib.\"))\n"
                + "  -> \\set Score.repeatCommands = #`((volta ,#{ \\markup { "
                + "\\volta-number \"1.2.\" ad lib. } #}))\n");
        }

        return s;
    }

    /// <summary>
    /// lilylib's <c>paren_matcher</c>, which rule 2.25.4 reaches through
    /// <c>lilylib.</c> — the same construction as
    /// <see cref="ParenMatcher(int)"/>, and named apart only to keep the call sites
    /// reading like upstream's.
    /// </summary>
    /// <param name="n">How deep the nesting may go.</param>
    /// <returns>The pattern.</returns>
    private static string BraceMatcherParen(int n) => ParenMatcher(n);

    /// <summary>Whether a split matches an expected sequence of words exactly.</summary>
    /// <param name="actual">The words.</param>
    /// <param name="expected">The words to compare with.</param>
    /// <returns>Whether they are the same.</returns>
    private static bool SameWords(string[] actual, params string[] expected)
    {
        if (actual.Length != expected.Length) { return false; }

        for (int i = 0; i < actual.Length; i++)
        {
            if (!string.Equals(actual[i], expected[i], System.StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    //convertrules.py:5135-5250
    private static string Rule_2_25_5(string s)
    {
        s = PythonRegex.Sub(
            "(text-)?font-defaults(?=\\s*\\.\\s*[\\w-]+\\s*=)", "property-defaults", s);
        if (s.Contains("font-defaults"))
        {
            StdErr(PythonRegex.Format(
                NotSmart, "advanced use of (text-)font-defaults"));
            StdErr("\nThe text-font-defaults and font-defaults variables have\n"
                + "been merged into a single property-defaults variable.\n");
            StdErr(UpdateManually);
        }

        const string gregorianWarning =
            "\nIdentifiers formerly in file 'gregorian.ly' are now always defined.  "
            + "The\nfollowing variable or variables in your LilyPond input file have "
            + "the same\nname as one of these identifiers:\n\n  {0}\n\n"
            + "It is recommended to rename them.\n";

        const string vaticanaScoreWarning =
            "\nThe use of 'gregorian.ly' is deprecated.  Code like\n\n"
            + "  \\input \"gregorian.ly\"\n\n  \\new VaticanaStaff { ... }\n\n"
            + "should be replaced with\n\n  \\new VaticanaScore {\n"
            + "    \\new VaticanaStaff { ... }\n  }\n\n  \\layout {\n"
            + "    indent = 0\n    ragged-last = ##t\n  }\n\n";

        string ancientRe =
            "(?x)\n  \\\\\n  ( IJ |\n    IIJ |\n    ij |\n    iij |\n\n"
            + "    versus |\n    responsum |\n\n    virga |\n    stropha |\n"
            + "    inclinatum |\n    auctum |\n    descendens |\n    ascendens |\n"
            + "    pes |\n    flexa |\n    oriscus |\n    quilisma |\n"
            + "    deminutum |\n    linea |\n    cavum |\n\n    virgula |\n"
            + "    divisioMinima |\n    divisioMaior |\n    divisioMaxima |\n"
            + "    finalis |\n\n    accentus |\n    ictus |\n    semicirculus |\n"
            + "    circulus |\n\n    augmentum |\n    ligature )\n  "
            + NameEndRe + "\n";

        if (PythonRegex.Search(
            "(?xm) ^ \\\\include \\s+ \"gregorian.ly\"", s).Success)
        {
            StdErr(PythonRegex.Format(NotSmart, "gregorian.ly to VaticanaScore"));
            StdErr(vaticanaScoreWarning);
            StdErr(UpdateManually);
        }
        else
        {
            //sorted(set(...)) -- python sorts strings by code point, which is what
            //Ordinal gives.
            List<string> found = PythonRegex.FindAll(ancientRe, s);
            SortedSet<string> keywords
                = new SortedSet<string>(found, System.StringComparer.Ordinal);
            if (keywords.Count > 0)
            {
                StdErr(gregorianWarning.Replace(
                    "{0}", string.Join(" ", keywords)));
            }
        }

        //For \markup \roman
        s = PythonRegex.Sub("\\\\roman\\b", "\\\\serif", s);

        //For #(make-roman-markup ...)
        s = PythonRegex.Sub("make-roman-markup", "make-serif-markup", s);

        //We don't convert (markup #:roman ...) because there would be a risk of false
        //positives, especially with advanced uses of set-global-fonts that convert-ly
        //might not have been able to replace.
        if (PythonRegex.Search("#:roman\\b(?!-)", s).Success)
        {
            StdErr(PythonRegex.Format(
                NotSmart,
                "possible use of \\roman markup command with #(markup ...) macro"));
            StdErr("\nconvert-ly detected \"#:roman\" in the input file.  This may be\n"
                + "related to using the \\roman markup command with the markup\n"
                + "macro, e.g., #(markup #:roman ...).  If this is the case, convert\n"
                + "#:roman to #:serif.\n");
        }

        //For \override
        s = PythonRegex.Sub("(font-family\\s*=\\s*[#$]')roman", "\\1serif", s);

        //For \tweak
        s = PythonRegex.Sub("(font-family\\s+[#$]')roman", "\\1serif", s);

        //For \markup \override
        s = PythonRegex.Sub(
            "(\\(\\s*font-family\\s+\\.\\s+)roman(\\s*\\))", "\\1serif\\2", s);

        //For fonts.roman = ...
        s = PythonRegex.Sub("(fonts\\s*\\.\\s*)roman(\\s*=\\s*)", "\\1serif\\2", s);

        return s;
    }
}
