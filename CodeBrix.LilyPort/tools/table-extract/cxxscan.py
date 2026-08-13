# Copyright (c) 2026 Jeremy Ellis and contributors
#
# CodeBrix.LilyPort is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.

"""Just enough C++ scanning to read LilyPond's registration macros.

The data the port needs out of lily/*.cc lives in macro argument lists --
ADD_TRANSLATOR, LY_DEFINE and their relatives -- and a regular expression is the
wrong tool for reading them. Three things defeat it, each of which was hit before
this module existed:

  * the four text blocks are usually R"(raw)" literals, but an empty one is ""
    and some are written as adjacent ordinary literals ("a " "b ");
  * a docstring may contain ")", so scanning to the first ");" truncates it; and
  * an argument may be a parenthesised C++ expression, (SCM a, SCM b), whose
    commas must not split the argument list.

So the scanner tracks literals and nesting properly. It is deliberately not a C++
parser: it knows literals, comments and parentheses, and nothing else.
"""


def skip_string_literal(text, start):
    """Returns (contents, index just past the literal) for the literal at start."""
    if text.startswith('R"(', start):
        end = text.index(')"', start + 3)
        return text[start + 3:end], end + 2

    if text[start] != '"':
        raise ValueError("not a string literal at " + str(start))

    index = start + 1
    parts = []
    while index < len(text):
        char = text[index]
        if char == "\\":
            # The macro text uses no interesting escapes; keep the escaped
            # character as itself rather than inventing a C++ unescaper.
            parts.append(text[index + 1])
            index += 2
            continue

        if char == '"':
            return "".join(parts), index + 1

        parts.append(char)
        index += 1

    raise ValueError("unterminated string literal")


def split_macro_arguments(text, start):
    """Splits a macro argument list, returning (arguments, index past the close).

    start is the index of the opening parenthesis.

    An argument that CONTAINS string literals yields exactly their contents joined
    together, which is both what C++ adjacent-literal concatenation produces and
    what keeps the whitespace between the comma and the opening quote -- the
    indentation LilyPond wraps these macros with -- out of the text. An argument
    with no literals yields its source characters, inner parentheses included.
    """
    arguments = []
    current = []
    literals = []
    depth = 0
    index = start

    def finish():
        return "".join(literals) if literals else "".join(current)

    while index < len(text):
        char = text[index]
        if char == '"' or text.startswith('R"(', index):
            contents, index = skip_string_literal(text, index)
            literals.append(contents)
            continue

        if text.startswith("/*", index):
            index = text.index("*/", index) + 2
            continue

        if char == "(":
            depth += 1
            if depth == 1:
                index += 1
                continue
        elif char == ")":
            depth -= 1
            if depth == 0:
                arguments.append(finish())
                return arguments, index + 1

        if char == "," and depth == 1:
            arguments.append(finish())
            current = []
            literals = []
            index += 1
            continue

        current.append(char)
        index += 1

    raise ValueError("unterminated macro argument list")


def collapse_whitespace(text):
    """Whitespace as the C preprocessor leaves it in a stringified argument.

    #ARGLIST turns every run of whitespace, newlines included, into one space --
    which matters because LilyPond writes long argument lists across two lines and
    the port has to produce the same string upstream's table holds.
    """
    return " ".join(text.split())
