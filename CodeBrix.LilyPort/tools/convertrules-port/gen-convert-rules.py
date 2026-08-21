#!/usr/bin/env python3
"""CodeBrix.LilyPort repo tool (ships nothing): converts LilyPond's own
`python/convertrules.py` into the C# the ConvertLy component runs.

WHY A GENERATOR AND NOT A HAND PORT. The file is 5,706 lines of 326 rules, and
the rules ARE their regular expressions: 282 of them are one or more `re.sub`
calls over patterns nobody should retype. The generator carries every pattern
and every replacement VERBATIM -- it reads the python STRING VALUE with
ast.literal_eval and writes that same value as a C# literal -- so no regex is
ever re-authored, and PythonRegex translates python's spelling to .NET's at
run time.

WHAT IT REFUSES TO DO. Anything it cannot translate with certainty it does NOT
guess at: the rule is listed in the report as HAND, and a hand-written
counterpart is expected in ConvertRules.Manual.cs. The generated table names
every rule in version order either way, so a missing hand port is a compile
error rather than a silently absent rule.

Reads the READ-ONLY LilyPond checkout (standing rule 3); writes
src/CodeBrix.LilyPort/ConvertLy/ConvertRules.g.cs.

Usage: PYTHONDONTWRITEBYTECODE=1 python3 gen-convert-rules.py [<lilypond-checkout>]
"""
import ast
import io
import os
import sys

CHECKOUT = os.path.expanduser(
    sys.argv[1] if len(sys.argv) > 1 else '~/GitHome/lilypond')
SOURCE = os.path.join(CHECKOUT, 'python', 'convertrules.py')
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(
    HERE, '..', '..', 'src', 'CodeBrix.LilyPort', 'ConvertLy', 'ConvertRules.g.cs'))

def cs_identifier(name):
    """A python module-level name as a C# member name."""
    return ''.join(part[:1].upper() + part[1:].lower() if part.isupper() else
                   part[:1].upper() + part[1:]
                   for part in name.split('_') if part)


class Untranslatable(Exception):
    """Raised for anything the generator will not guess at."""


def cs_string(value):
    """A python str as a C# string literal."""
    out = ['"']
    for ch in value:
        if ch == '"':
            out.append('\\"')
        elif ch == '\\':
            out.append('\\\\')
        elif ch == '\n':
            out.append('\\n')
        elif ch == '\r':
            out.append('\\r')
        elif ch == '\t':
            out.append('\\t')
        elif ord(ch) < 32 or ord(ch) == 127:
            out.append('\\u%04x' % ord(ch))
        else:
            out.append(ch)
    out.append('"')
    return ''.join(out)


def call_name(node):
    f = node.func
    if isinstance(f, ast.Attribute):
        if isinstance(f.value, ast.Name):
            return f.value.id + '.' + f.attr
        return '<expr>.' + f.attr
    return getattr(f, 'id', None)


class Translator:
    def __init__(self, constants, subject='s', match_param=None):
        self.constants = constants
        # The name the rule's document text goes by. An inner replacement function
        # reassigns `s' as a LOCAL, and C# forbids shadowing an enclosing local, so an
        # inner translator calls it `text' instead.
        self.subject = subject
        self.match_param = match_param
        self.functions = {}
        self.lines = []
        # Local variables the rule declares, and what each one holds -- 'match' for a
        # re.search/re.match result (which needs .Success in boolean context, where
        # python just asks whether the object is None) and 'value' for everything else.
        self.locals = {}

    # ---- expressions -------------------------------------------------
    def expr(self, node):
        if isinstance(node, ast.Constant):
            if isinstance(node.value, str):
                return cs_string(node.value)
            if isinstance(node.value, bool):
                return 'true' if node.value else 'false'
            if isinstance(node.value, int):
                return str(node.value)
            raise Untranslatable('constant %r' % (node.value,))

        if isinstance(node, ast.Name):
            if node.id == 's':
                return self.subject
            if node.id == self.match_param:
                return node.id
            if node.id in self.functions:
                return self.functions[node.id]
            if node.id in self.locals:
                return node.id
            if node.id in self.constants:
                return self.constants[node.id]
            raise Untranslatable('name %s' % node.id)

        if isinstance(node, ast.Compare):
            if len(node.ops) != 1 or len(node.comparators) != 1:
                raise Untranslatable('chained comparison')
            operators = {
                ast.Eq: '==', ast.NotEq: '!=',
                ast.Lt: '<', ast.LtE: '<=', ast.Gt: '>', ast.GtE: '>=',
            }
            if isinstance(node.ops[0], (ast.In, ast.NotIn)):
                membership = '%s.Contains(%s)' % (
                    self.expr(node.comparators[0]), self.expr(node.left))
                return membership if isinstance(node.ops[0], ast.In) \
                    else '!' + membership
            operator = operators.get(type(node.ops[0]))
            if operator is None:
                raise Untranslatable('comparison %s' % type(node.ops[0]).__name__)
            return '(%s %s %s)' % (
                self.expr(node.left), operator, self.expr(node.comparators[0]))

        if isinstance(node, ast.BinOp) and isinstance(node.op, ast.Add):
            return '(%s + %s)' % (self.expr(node.left), self.expr(node.right))

        if isinstance(node, ast.BinOp) and isinstance(node.op, ast.Mult):
            # python overloads `*': `text * n' repeats a sub-pattern (used to build
            # matcharg-style expressions) while `2 * n' is arithmetic. Which one this is
            # has to be decided from the OPERANDS -- getting it wrong compiles happily
            # in python and not at all here, which is how it was caught.
            left, right = node.left, node.right
            if isinstance(right, ast.Constant) and isinstance(right.value, int) \
                    and self.is_text(left):
                return 'Repeat(%s, %d)' % (self.expr(left), right.value)
            if isinstance(left, ast.Constant) and isinstance(left.value, int) \
                    and self.is_text(right):
                return 'Repeat(%s, %d)' % (self.expr(right), left.value)
            return '(%s * %s)' % (self.expr(left), self.expr(right))

        if isinstance(node, ast.JoinedStr):
            # An f-string: a concatenation of literal pieces and formatted values.
            pieces = []
            for part in node.values:
                if isinstance(part, ast.Constant):
                    pieces.append(cs_string(part.value))
                elif isinstance(part, ast.FormattedValue):
                    if part.format_spec is not None or part.conversion not in (-1, None):
                        raise Untranslatable('f-string conversion')
                    pieces.append(self.expr(part.value))
                else:
                    raise Untranslatable('f-string part')
            return '(' + ' + '.join(pieces) + ')' if pieces else '""'

        if isinstance(node, ast.BinOp) and isinstance(node.op, ast.Mod):
            values = node.right
            args = list(values.elts) if isinstance(values, ast.Tuple) else [values]
            return 'PythonRegex.Format(%s, %s)' % (
                self.expr(node.left), ', '.join(self.expr(a) for a in args))

        if isinstance(node, ast.BoolOp):
            joiner = ' || ' if isinstance(node.op, ast.Or) else ' && '
            return '(' + joiner.join(self.test(v) for v in node.values) + ')'

        if isinstance(node, ast.UnaryOp) and isinstance(node.op, ast.Not):
            return '!(%s)' % self.test(node.operand)

        if isinstance(node, ast.Call):
            return self.call(node)

        raise Untranslatable(type(node).__name__)

    def is_text(self, node):
        """Whether an expression is a STRING, structurally.

        python overloads `*': `text * n' repeats, `2 * n' multiplies. Only the operands
        say which, and C# will not accept the wrong choice -- which is how the one
        mistranslation this generator made was caught (`2*int(match.group(1))').
        """
        if isinstance(node, ast.Constant):
            return isinstance(node.value, str)
        if isinstance(node, ast.Name):
            return node.id in self.constants
        if isinstance(node, ast.JoinedStr):
            return True
        if isinstance(node, ast.BinOp) and isinstance(node.op, ast.Add):
            return self.is_text(node.left) or self.is_text(node.right)
        if isinstance(node, ast.BinOp) and isinstance(node.op, ast.Mod):
            return self.is_text(node.left)
        if isinstance(node, ast.BinOp) and isinstance(node.op, ast.Mult):
            return self.is_text(node.left) or self.is_text(node.right)
        if isinstance(node, ast.Call):
            return call_name(node) in (
                'paren_matcher', 'brace_matcher', 'lilylib.brace_matcher',
                're.sub', '_')
        return False

    def call(self, node):
        name = call_name(node)

        if name == '_':
            # gettext. convert-ly's own messages are not translated by this port --
            # the app shows them through its own i18n service -- so the marker is
            # the identity it already is when no catalog is loaded.
            return self.expr(node.args[0])

        if name == 're.sub':
            if len(node.args) != 3:
                raise Untranslatable('re.sub/%d' % len(node.args))
            replacement = node.args[1]
            if isinstance(replacement, ast.Name) and replacement.id in self.functions:
                return 'PythonRegex.Sub(%s, %s, %s)' % (
                    self.expr(node.args[0]), self.functions[replacement.id],
                    self.expr(node.args[2]))
            return 'PythonRegex.Sub(%s, %s, %s)' % (
                self.expr(node.args[0]), self.expr(node.args[1]), self.expr(node.args[2]))

        if name == 're.search':
            if len(node.args) != 2:
                raise Untranslatable('re.search/%d' % len(node.args))
            return 'PythonRegex.Search(%s, %s)' % (
                self.expr(node.args[0]), self.expr(node.args[1]))

        if name == 're.match':
            return 'PythonRegex.MatchAt(%s, %s)' % (
                self.expr(node.args[0]), self.expr(node.args[1]))

        # A method on the MATCH object, or on a string.
        if isinstance(node.func, ast.Attribute):
            attribute = node.func.attr
            base_node = node.func.value
            is_match = (isinstance(base_node, ast.Name)
                        and base_node.id == self.match_param)

            if is_match and attribute == 'group':
                if not node.args:
                    return base_node.id + '.Value'
                index = node.args[0]
                if isinstance(index, ast.Constant) and index.value == 0:
                    return base_node.id + '.Value'
                return '%s.Groups[%s].Value' % (base_node.id, self.expr(index))

            if attribute in ('lower', 'upper') and not node.args:
                return '%s.To%sInvariant()' % (
                    self.expr(base_node), attribute.capitalize())

            if attribute == 'strip' and not node.args:
                return '%s.Trim()' % self.expr(base_node)

        if name in ('paren_matcher',) and len(node.args) == 1:
            return 'ParenMatcher(%s)' % self.expr(node.args[0])

        if name in ('brace_matcher', 'lilylib.brace_matcher') and len(node.args) == 1:
            return 'BraceMatcher(%s)' % self.expr(node.args[0])

        if name == 're.compile' and len(node.args) == 1:
            return 'PythonRegex.Compile(%s)' % self.expr(node.args[0])

        if name in ('int',) and len(node.args) == 1:
            return 'PythonRegex.ToInt(%s)' % self.expr(node.args[0])

        if name in ('len',) and len(node.args) == 1:
            return '%s.Length' % self.expr(node.args[0])

        if name in ('s.replace', '<expr>.replace'):
            target = self.expr(node.func.value)
            return '%s.Replace(%s, %s)' % (
                target, self.expr(node.args[0]), self.expr(node.args[1]))

        if name == 'stderr_write':
            return 'StdErr(%s)' % self.expr(node.args[0])

        if name == 'warning':
            return 'Warning(%s)' % self.expr(node.args[0])

        raise Untranslatable('call %s' % name)

    def test(self, node):
        """An expression in BOOLEAN context, where python uses truthiness."""
        if isinstance(node, ast.Call) and call_name(node) in ('re.search', 're.match'):
            return self.expr(node) + '.Success'
        if isinstance(node, ast.Name) and self.locals.get(node.id) == 'match':
            return node.id + '.Success'
        if isinstance(node, ast.Compare):
            return self.expr(node)
        if isinstance(node, ast.BoolOp):
            joiner = ' || ' if isinstance(node.op, ast.Or) else ' && '
            return '(' + joiner.join(self.test(v) for v in node.values) + ')'
        if isinstance(node, ast.UnaryOp) and isinstance(node.op, ast.Not):
            return '!(%s)' % self.test(node.operand)
        if isinstance(node, ast.Compare):
            raise Untranslatable('comparison')
        return self.expr(node)

    # ---- statements --------------------------------------------------
    def block(self, body, indent):
        for stmt in body:
            self.stmt(stmt, indent)

    def stmt(self, node, indent):
        pad = ' ' * indent

        if isinstance(node, ast.Expr):
            if isinstance(node.value, ast.Constant):
                return  # a docstring
            self.lines.append(pad + self.expr(node.value) + ';')
            return

        if isinstance(node, ast.Assign):
            if len(node.targets) != 1 or not isinstance(node.targets[0], ast.Name):
                raise Untranslatable('assignment target')
            name = node.targets[0].id
            value = self.expr(node.value)
            if name == 's':
                if self.subject != 's' and self.subject not in self.locals:
                    # The inner function's first assignment DECLARES its local.
                    self.locals[self.subject] = 'value'
                    self.lines.append(pad + 'var ' + self.subject + ' = ' + value + ';')
                    return
                self.lines.append(pad + self.subject + ' = ' + value + ';')
                return

            kind = 'match' if (
                isinstance(node.value, ast.Call)
                and call_name(node.value) in ('re.search', 're.match')) else 'value'
            if name in self.locals:
                self.lines.append(pad + name + ' = ' + value + ';')
            else:
                # `var', so the generator never has to infer a C# type -- the compiler
                # does it, and a local that changes shape is a compile error here rather
                # than a wrong answer at run time.
                self.locals[name] = kind
                self.lines.append(pad + 'var ' + name + ' = ' + value + ';')
            return

        if isinstance(node, ast.Return):
            if node.value is None:
                self.lines.append(pad + 'return s;')
            else:
                self.lines.append(pad + 'return ' + self.expr(node.value) + ';')
            return

        if isinstance(node, ast.If):
            self.lines.append(pad + 'if (' + self.test(node.test) + ')')
            self.lines.append(pad + '{')
            self.block(node.body, indent + 4)
            self.lines.append(pad + '}')
            if node.orelse:
                self.lines.append(pad + 'else')
                self.lines.append(pad + '{')
                self.block(node.orelse, indent + 4)
                self.lines.append(pad + '}')
            return

        if isinstance(node, ast.Raise):
            exc = node.exc
            name = call_name(exc) if isinstance(exc, ast.Call) else getattr(exc, 'id', None)
            if name != 'FatalConversionError':
                raise Untranslatable('raise %s' % name)
            self.lines.append(pad + 'throw new FatalConversionError();')
            return

        if isinstance(node, ast.FunctionDef):
            if len(node.args.args) != 1:
                raise Untranslatable('inner function of %d arguments'
                                     % len(node.args.args))
            parameter = node.args.args[0].arg
            inner = Translator(self.constants, subject='text', match_param=parameter)
            inner.block(node.body, indent + 4)
            name = cs_identifier(node.name)
            self.functions[node.name] = name
            self.lines.append(
                pad + 'string %s(System.Text.RegularExpressions.Match %s)'
                % (name, parameter))
            self.lines.append(pad + '{')
            self.lines.extend(inner.lines)
            self.lines.append(pad + '}')
            self.lines.append('')
            return

        if isinstance(node, ast.Pass):
            return

        raise Untranslatable(type(node).__name__)


def rule_id(version, ordinal=0):
    """The C# method name for a rule.

    Two versions carry TWO rules each in upstream's list (1.3.138 and 2.7.32), so the
    name has to disambiguate or the second would silently overwrite the first.
    """
    name = 'Rule_' + '_'.join(str(p) for p in version)
    return name if ordinal == 0 else '%s_%d' % (name, ordinal + 1)


def main():
    src = io.open(SOURCE, encoding='utf-8').read()
    tree = ast.parse(src)

    # Module-level string constants the rules mention -- the shared sub-patterns
    # (matchstring, matcharg, wordsyntax, before_id, barstring...) and the long warning
    # texts. They are discovered rather than listed, so a LilyPond version that adds one
    # needs no edit here; source order is kept, which is also their dependency order.
    # Names the HAND-WRITTEN file owns, and the generator must not also emit.
    # should_really_be_music_function is MUTATED by record_ugly, so it cannot be a
    # readonly field; the rest are functions rather than data.
    OWNED_BY_HAND = {'should_really_be_music_function'}

    constants = {}
    constant_lines = []
    probe = Translator(constants)
    for node in tree.body:
        if not (isinstance(node, ast.Assign) and len(node.targets) == 1
                and isinstance(node.targets[0], ast.Name)):
            continue
        try:
            value = probe.expr(node.value)
        except Untranslatable:
            continue
        if not value.startswith('"') and '(' not in value:
            continue
        if node.targets[0].id in OWNED_BY_HAND:
            constants[node.targets[0].id] = cs_identifier(node.targets[0].id)
            continue
        name = cs_identifier(node.targets[0].id)
        constants[node.targets[0].id] = name
        constant_lines.append((node.targets[0].id, name, value))

    rules = []
    seen = {}
    for node in tree.body:
        if isinstance(node, ast.FunctionDef) and node.decorator_list:
            dec = node.decorator_list[0]
            if isinstance(dec, ast.Call) and getattr(dec.func, 'id', None) == 'rule':
                version = ast.literal_eval(dec.args[0])
                message = ast.literal_eval(dec.args[1]) \
                    if isinstance(dec.args[1], ast.Constant) else None
                ordinal = seen.get(version, 0)
                seen[version] = ordinal + 1
                rules.append((version, message, node, ordinal))

    generated, hand = [], []
    bodies = []
    for version, message, node, ordinal in rules:
        if message is None:
            hand.append((version, ordinal, 'non-literal message'))
            continue
        translator = Translator(constants)
        try:
            translator.block(node.body, 8)
        except Untranslatable as why:
            hand.append((version, ordinal, str(why)))
            continue
        generated.append((version, message))
        bodies.append((version, message, ordinal, translator.lines))

    out = io.StringIO()
    out.write('''// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// <auto-generated>
//     GENERATED FILE -- DO NOT EDIT BY HAND.
//
//     Written by tools/convertrules-port/gen-convert-rules.py from LilyPond's own
//     python/convertrules.py. Every pattern and every replacement below is the
//     VERBATIM python string value; PythonRegex translates python's spelling of a
//     regular expression to .NET's at run time. Re-run the tool after a LilyPond
//     version bump; the rules a version adds appear at the end of the table.
//
//     Rules that the generator will not translate with certainty are hand-written
//     in ConvertRules.Manual.cs and named by the table here, so a missing one is a
//     compile error and never a silently absent rule.
// </auto-generated>

using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CodeBrix.LilyPort.ConvertLy;

internal static partial class ConvertRules
{
''')

    for original, name, value in constant_lines:
        out.write('    /// <summary>convertrules.py\'s <c>%s</c>.</summary>\n' % original)
        # internal, not private: an unused private field is CS0414, and which
        # constants a given LilyPond version's rules use is not the generator's to know.
        out.write('    internal static readonly %s %s\n        = %s;\n\n'
                  % ('Regex' if value.startswith('PythonRegex.Compile') else 'string',
                     name, value))

    out.write('    /// <summary>Every conversion rule, in the order upstream declares them.</summary>\n')
    out.write('    internal static IReadOnlyList<ConversionRule> All { get; }\n')
    out.write('        = new List<ConversionRule>\n        {\n')
    for version, message, node, ordinal in rules:
        out.write('            new ConversionRule(\n')
        out.write('                new ConversionVersion(%s),\n' % ', '.join(str(p) for p in version))
        out.write('                %s,\n' % cs_string(message if message is not None else ''))
        out.write('                %s),\n' % rule_id(version, ordinal))
    out.write('        };\n')

    for version, message, ordinal, lines in bodies:
        out.write('\n')
        out.write('    /// <summary>%s%s</summary>\n' % (
            '.'.join(str(p) for p in version),
            '' if ordinal == 0 else ' (the %d%s rule at that version)' % (
                ordinal + 1, 'nd' if ordinal == 1 else 'th')))
        out.write('    /// <param name="s">The document text.</param>\n')
        out.write('    /// <returns>The converted text.</returns>\n')
        out.write('    private static string %s(string s)\n    {\n' % rule_id(version, ordinal))
        for line in lines:
            out.write(line.rstrip() + '\n')
        out.write('    }\n')

    out.write('}\n')

    with io.open(OUT, 'w', encoding='utf-8', newline='\n') as handle:
        handle.write(out.getvalue())

    print('rules total     : %d' % len(rules))
    print('generated       : %d' % len(generated))
    print('hand-written    : %d' % len(hand))
    print('written         : %s' % OUT)
    print()
    print('HAND-WRITTEN RULES EXPECTED IN ConvertRules.Manual.cs:')
    for version, ordinal, why in hand:
        print('    %-12s %-22s // %s' % (
            '.'.join(str(p) for p in version), rule_id(version, ordinal), why))


if __name__ == '__main__':
    main()
