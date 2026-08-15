/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
                 Jan Nieuwenhuizen <janneke@gnu.org>

  LilyPond is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  LilyPond is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with LilyPond.  If not, see <http://www.gnu.org/licenses/>.
*/

using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (lines 1024-1319);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <content>
/// Book, bookpart and score blocks: <c>book_block</c>,
/// <c>book_body</c>, <c>bookpart_block</c>, <c>bookpart_body</c>,
/// <c>score_block</c>, <c>score_body</c>, <c>score_items</c>, plus the three
/// mid-rule actions (<c>$@5</c>–<c>$@7</c>) those rules carry — the ones that open a
/// <c>\header</c> scope on the block being built. (<c>score_item</c> is
/// pass-through and carries no actions.) These bodies construct and populate the
/// Engine's <see cref="Book"/> and <see cref="Score"/>, which were ported for this
/// group, so they cast directly rather than going through host predicates — the
/// OutputDef precedent from the TopLevel group.
/// </content>
public static partial class LilyPondRuleActions
{
    private static void RegisterBookBlocks(RuleActionTable table)
    {
        // ------ book_block (parser.yy 1024-1031) ------

        // book_block: BOOK '{' book_body '}' {
        //     $$ = $3;
        //     unsmob<Book> ($$)->origin ()->set_spot (@$);
        //     pop_paper (parser);
        //     parser->lexer_->set_identifier ("$current-book", SCM_BOOL_F); }
        table.Add(
            "book_block: BOOK '{' book_body '}'",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                ((Book)values[2]).SetSpot(host.SchemeLocation(location));
                ParserActionHelpers.PopPaper(host);
                host.SetIdentifier(Symbol.Intern("$current-book"), false);
                return values[2];
            });

        // ------ book_body (parser.yy 1036-1115) ------
        //
        // Upstream carries this FIXME here:
        // /* FIXME:
        //    * Use 'handlers' like for toplevel-* stuff?
        //    * grok \layout and \midi?  */

        // book_body: /* empty */ — a \book opens on a fresh Book carrying a CLONE of
        // $defaultpaper (pushed as the $papers top) and a copy-seeded header, and
        // announces itself as $current-book.
        table.Add(
            "book_body: /* empty */",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                Book book = new Book();
                ParserActionHelpers.InitPapers(host);
                book.Paper = ((OutputDef)host.LookupIdentifier("$defaultpaper")).Clone();
                ParserActionHelpers.PushPaper(host, book.Paper);
                book.Header = ParserActionHelpers.GetHeader(host);
                host.SetIdentifier(Symbol.Intern("$current-book"), book);
                return book;
            });

        // book_body: BOOK_IDENTIFIER {
        //     parser->lexer_->set_identifier ("$current-book", $1); }
        table.Add(
            "book_body: BOOK_IDENTIFIER",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context)
                    .SetIdentifier(Symbol.Intern("$current-book"), values[0]);
                return values[0];
            });

        // book_body: book_body paper_block {
        //     unsmob<Book> ($1)->paper_ = unsmob<Output_def> ($2);
        //     set_paper (parser, unsmob<Output_def> ($2)); }
        table.Add(
            "book_body: book_body paper_block",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                ((Book)values[0]).Paper = (OutputDef)values[1];
                ParserActionHelpers.SetPaper(host, (OutputDef)values[1]);
                return values[0];
            });

        // book_body: book_body bookpart_block {
        //     SCM proc = parser->lexer_->lookup_identifier ("book-bookpart-handler");
        //     ly_call (proc, $1, $2); }
        table.Add(
            "book_body: book_body bookpart_block",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                host.Call(host.LookupIdentifier("book-bookpart-handler"), values[0], values[1]);
                return values[0];
            });

        // book_body: book_body score_block
        table.Add(
            "book_body: book_body score_block",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                host.Call(host.LookupIdentifier("book-score-handler"), values[0], values[1]);
                return values[0];
            });

        // book_body: book_body composite_music
        table.Add(
            "book_body: book_body composite_music",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                host.Call(host.LookupIdentifier("book-music-handler"), values[0], values[1]);
                return values[0];
            });

        // book_body: book_body full_markup — the handler receives a LIST holding the
        // one markup, as at top level.
        table.Add(
            "book_body: book_body full_markup",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                host.Call(
                    host.LookupIdentifier("book-text-handler"),
                    values[0],
                    new Pair(values[1], Nil.Instance));
                return values[0];
            });

        // book_body: book_body full_markup_list
        table.Add(
            "book_body: book_body full_markup_list",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                host.Call(host.LookupIdentifier("book-text-handler"), values[0], values[1]);
                return values[0];
            });

        // book_body: book_body SCM_TOKEN {
        //     // Evaluate and ignore #xxx, as opposed to \xxx
        //     parser->lexer_->eval_scm_token ($2, @2); }
        table.Add(
            "book_body: book_body SCM_TOKEN",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context)
                    .EvalSchemeToken(values[1], locations[1]);
                return values[0];
            });

        // book_body: book_body embedded_scm_active — classify what a #-expression in
        // a book body produced: markup(-list), score, paper block (anything else
        // \paper-shaped is an error), or header module; anything else but
        // SCM_UNSPECIFIED is a bad expression type.
        table.Add(
            "book_body: book_body embedded_scm_active",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object value = values[1];

                object markup = DefaultArgument.Instance;
                if (host.IsMarkup(value))
                {
                    markup = new Pair(value, Nil.Instance);
                }
                else if (host.IsMarkupList(value))
                {
                    markup = value;
                }

                if (markup is Pair)
                {
                    host.Call(host.LookupIdentifier("book-text-handler"), values[0], markup);
                }
                else if (value is Score)
                {
                    host.Call(host.LookupIdentifier("book-score-handler"), values[0], value);
                }
                else if (value is OutputDef outputDef)
                {
                    if (ReferenceEquals(
                            outputDef.CVariable("output-def-kind"),
                            Symbol.Intern("paper")))
                    {
                        ((Book)values[0]).Paper = outputDef;
                        ParserActionHelpers.SetPaper(host, outputDef);
                    }
                    else
                    {
                        ParserActionHelpers.ParserError(
                            context, locations[1], "need \\paper for paper block");
                    }
                }
                else if (host.IsModule(value))
                {
                    host.ModuleCopy(((Book)values[0]).Header, value);
                }
                else if (!(value is Unspecified))
                {
                    ParserActionHelpers.ParserError(context, locations[1], "bad expression type");
                }

                return values[0];
            });

        // The mid-rule action of `book_body: book_body { ... } lilypond_header`:
        // { parser->lexer_->add_scope (unsmob<Book> ($1)->header_); } — a \header
        // inside a \book opens directly on the book's own header module. $1 is the
        // book_body already ON THE STACK below this empty reduction, which is what
        // ParseContext.StackValue reaches. The host rule itself carries no action
        // (Bison's default $$ = $1 is the whole behaviour).
        table.Add(
            "$@5: /* empty */",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context)
                    .AddScope(((Book)context.StackValue(0)).Header);
                return Unspecified.Instance;
            });

        // book_body: book_body error — error recovery drops the book's content so
        // the rest of the file still parses against a consistent object.
        table.Add(
            "book_body: book_body error",
            (context, values, locations, location) =>
            {
                Book book = (Book)values[0];
                book.Paper = null;
                book.Scores = Nil.Instance;
                book.Bookparts = Nil.Instance;
                return values[0];
            });

        // ------ bookpart_block (parser.yy 1119-1125) ------

        // bookpart_block: BOOKPART '{' bookpart_body '}' {
        //     $$ = $3;
        //     unsmob<Book> ($$)->origin ()->set_spot (@$);
        //     parser->lexer_->set_identifier ("$current-bookpart", SCM_BOOL_F); }
        table.Add(
            "bookpart_block: BOOKPART '{' bookpart_body '}'",
            (context, values, locations, location) =>
            {
                ((Book)values[2]).SetSpot(
                    ParserActionHelpers.RequireHost(context).SchemeLocation(location));
                ParserActionHelpers.RequireHost(context)
                    .SetIdentifier(Symbol.Intern("$current-bookpart"), false);
                return values[2];
            });

        // ------ bookpart_body (parser.yy 1127-1200) ------

        // bookpart_body: /* empty */ — a bookpart is a BARE Book: no paper stack
        // work, and its header stays the empty list until something needs it.
        table.Add(
            "bookpart_body: /* empty */",
            (context, values, locations, location) =>
            {
                Book book = new Book();
                ParserActionHelpers.RequireHost(context)
                    .SetIdentifier(Symbol.Intern("$current-bookpart"), book);
                return book;
            });

        // bookpart_body: BOOK_IDENTIFIER {
        //     parser->lexer_->set_identifier ("$current-bookpart", $1); }
        table.Add(
            "bookpart_body: BOOK_IDENTIFIER",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context)
                    .SetIdentifier(Symbol.Intern("$current-bookpart"), values[0]);
                return values[0];
            });

        // bookpart_body: bookpart_body paper_block {
        //     unsmob<Book> ($$)->paper_ = unsmob<Output_def> ($2); } — no $papers
        // work here: the stack belongs to the enclosing \book.
        table.Add(
            "bookpart_body: bookpart_body paper_block",
            (context, values, locations, location) =>
            {
                ((Book)values[0]).Paper = (OutputDef)values[1];
                return values[0];
            });

        // bookpart_body: bookpart_body score_block
        table.Add(
            "bookpart_body: bookpart_body score_block",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                host.Call(host.LookupIdentifier("bookpart-score-handler"), values[0], values[1]);
                return values[0];
            });

        // bookpart_body: bookpart_body composite_music
        table.Add(
            "bookpart_body: bookpart_body composite_music",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                host.Call(host.LookupIdentifier("bookpart-music-handler"), values[0], values[1]);
                return values[0];
            });

        // bookpart_body: bookpart_body full_markup
        table.Add(
            "bookpart_body: bookpart_body full_markup",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                host.Call(
                    host.LookupIdentifier("bookpart-text-handler"),
                    values[0],
                    new Pair(values[1], Nil.Instance));
                return values[0];
            });

        // bookpart_body: bookpart_body full_markup_list
        table.Add(
            "bookpart_body: bookpart_body full_markup_list",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                host.Call(host.LookupIdentifier("bookpart-text-handler"), values[0], values[1]);
                return values[0];
            });

        // bookpart_body: bookpart_body SCM_TOKEN {
        //     // Evaluate and ignore #xxx, as opposed to \xxx
        //     parser->lexer_->eval_scm_token ($2, @2); }
        table.Add(
            "bookpart_body: bookpart_body SCM_TOKEN",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context)
                    .EvalSchemeToken(values[1], locations[1]);
                return values[0];
            });

        // bookpart_body: bookpart_body embedded_scm_active — as for book_body, except
        // that a paper block does not touch the $papers stack, and the header module
        // is created on demand before being merged into.
        table.Add(
            "bookpart_body: bookpart_body embedded_scm_active",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object value = values[1];

                object markup = DefaultArgument.Instance;
                if (host.IsMarkup(value))
                {
                    markup = new Pair(value, Nil.Instance);
                }
                else if (host.IsMarkupList(value))
                {
                    markup = value;
                }

                if (markup is Pair)
                {
                    host.Call(host.LookupIdentifier("bookpart-text-handler"), values[0], markup);
                }
                else if (value is Score)
                {
                    host.Call(host.LookupIdentifier("bookpart-score-handler"), values[0], value);
                }
                else if (value is OutputDef outputDef)
                {
                    if (ReferenceEquals(
                            outputDef.CVariable("output-def-kind"),
                            Symbol.Intern("paper")))
                    {
                        ((Book)values[0]).Paper = outputDef;
                    }
                    else
                    {
                        ParserActionHelpers.ParserError(
                            context, locations[1], "need \\paper for paper block");
                    }
                }
                else if (host.IsModule(value))
                {
                    Book book = (Book)values[0];
                    if (!host.IsModule(book.Header))
                    {
                        book.Header = host.MakeModule();
                    }

                    host.ModuleCopy(book.Header, value);
                }
                else if (!(value is Unspecified))
                {
                    ParserActionHelpers.ParserError(context, locations[1], "bad expression type");
                }

                return values[0];
            });

        // The mid-rule action of `bookpart_body: bookpart_body { ... } lilypond_header`:
        // a bookpart's header module is created ON DEMAND — it stays the empty list
        // until a \header (or a header-shaped Scheme value) arrives. The host rule
        // carries no action.
        table.Add(
            "$@6: /* empty */",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                Book book = (Book)context.StackValue(0);
                if (!host.IsModule(book.Header))
                {
                    book.Header = host.MakeModule();
                }

                host.AddScope(book.Header);
                return Unspecified.Instance;
            });

        // bookpart_body: bookpart_body error — as book_body's recovery, except that a
        // bookpart has no bookparts of its own to drop.
        table.Add(
            "bookpart_body: bookpart_body error",
            (context, values, locations, location) =>
            {
                Book book = (Book)values[0];
                book.Paper = null;
                book.Scores = Nil.Instance;
                return values[0];
            });

        // ------ score_block (parser.yy 1203-1208) ------

        // score_block: SCORE '{' score_body '}' {
        //     unsmob<Score> ($3)->origin ()->set_spot (@$);
        //     $$ = $3; }
        table.Add(
            "score_block: SCORE '{' score_body '}'",
            (context, values, locations, location) =>
            {
                ((Score)values[2]).SetSpot(
                    ParserActionHelpers.RequireHost(context).SchemeLocation(location));
                return values[2];
            });

        // ------ score_body (parser.yy 1210-1231) ------

        // score_body: score_items — when the items never produced a Score there was
        // no music: report it, and salvage a fresh Score carrying whatever header
        // and output definitions the items DID collect (module at the head of the
        // list, output definitions after it, most recent first).
        table.Add(
            "score_body: score_items",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (!(values[0] is Score))
                {
                    ParserActionHelpers.ParserError(
                        context, locations[0], "Missing music in \\score");
                    Score score = new Score();
                    object items = values[0];
                    if (items is Pair pair && host.IsModule(pair.Car))
                    {
                        score.SetHeader(pair.Car);
                        items = pair.Cdr;
                    }

                    for (object p = ParserActionHelpers.ReverseInPlace(items, Nil.Instance);
                         p is Pair entry;
                         p = entry.Cdr)
                    {
                        score.AddOutputDef(entry.Car as OutputDef);
                    }

                    return score;
                }

                return values[0];
            });

        // score_body: score_body error { unsmob<Score> ($$)->error_found_ = true; }
        table.Add(
            "score_body: score_body error",
            (context, values, locations, location) =>
            {
                ((Score)values[0]).ErrorFound = true;
                return values[0];
            });

        // ------ score_items (parser.yy 1239-1318) ------
        //
        // score_item itself (embedded_scm | music | output_def) is pure pass-through
        // and carries no actions.

        // score_items: /* empty */ { $$ = SCM_EOL; }
        table.Add(
            "score_items: /* empty */",
            (context, values, locations, location) => Nil.Instance);

        // score_items: score_items score_item — the accumulator. Until music arrives,
        // $$ is a LIST (optional header module at the head, then output definitions,
        // most recent first); the first music is scorified and the collected list is
        // folded into the resulting Score, which then IS $$ from that point on.
        // \paper is refused here; a spurious expression is an error.
        table.Add(
            "score_items: score_items score_item",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object result = values[0];
                object item = values[1];

                OutputDef outputDef = item as OutputDef;
                if (outputDef != null)
                {
                    if (ReferenceEquals(
                            outputDef.CVariable("output-def-kind"),
                            Symbol.Intern("paper")))
                    {
                        ParserActionHelpers.ParserError(
                            context,
                            locations[1],
                            "\\paper cannot be used in \\score, use \\layout instead");
                        outputDef = null;
                        item = Unspecified.Instance;
                    }
                }
                else if (!(result is Score))
                {
                    if (item is MusicObject)
                    {
                        item = host.ScorifyMusic(item);
                    }

                    if (item is Score)
                    {
                        result = item;
                        item = Unspecified.Instance;
                    }
                }

                Score score = result as Score;
                object collected = values[0];
                if (score != null && collected is Pair)
                {
                    if (host.IsModule(((Pair)collected).Car))
                    {
                        score.SetHeader(((Pair)collected).Car);
                        collected = ((Pair)collected).Cdr;
                    }

                    for (object p = ParserActionHelpers.ReverseInPlace(collected, Nil.Instance);
                         p is Pair entry;
                         p = entry.Cdr)
                    {
                        score.AddOutputDef(entry.Car as OutputDef);
                    }
                }

                if (outputDef != null)
                {
                    if (score != null)
                    {
                        score.AddOutputDef(outputDef);
                    }
                    else if (result is Pair resultPair && host.IsModule(resultPair.Car))
                    {
                        resultPair.Cdr = new Pair(item, resultPair.Cdr);
                    }
                    else
                    {
                        result = new Pair(item, result);
                    }
                }
                else if (host.IsModule(item))
                {
                    object module = Unspecified.Instance;
                    if (score != null)
                    {
                        module = score.GetHeader();
                        if (!host.IsModule(module))
                        {
                            module = host.MakeModule();
                            score.SetHeader(module);
                        }
                    }
                    else if (result is Pair resultPair && host.IsModule(resultPair.Car))
                    {
                        module = resultPair.Car;
                    }
                    else
                    {
                        module = host.MakeModule();
                        result = new Pair(module, result);
                    }

                    host.ModuleCopy(module, item);
                }
                else if (!(item is Unspecified))
                {
                    ParserActionHelpers.ParserError(
                        context, locations[1], "Spurious expression in \\score");
                }

                return result;
            });

        // The mid-rule action of `score_items: score_items { ... } lilypond_header`:
        // open the \header scope on the score's header module (created on demand) —
        // or, before any music has arrived, on a module at the head of the collected
        // list, CONSING one on when the list does not have one yet. That cons
        // ASSIGNS $1, which is why ParseContext.SetStackValue exists.
        table.Add(
            "$@7: /* empty */",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object items = context.StackValue(0);
                if (items is Score score)
                {
                    if (!host.IsModule(score.GetHeader()))
                    {
                        score.SetHeader(host.MakeModule());
                    }

                    host.AddScope(score.GetHeader());
                }
                else
                {
                    if (!(items is Pair pair) || !host.IsModule(pair.Car))
                    {
                        items = new Pair(host.MakeModule(), items);
                        context.SetStackValue(0, items);
                    }

                    host.AddScope(((Pair)items).Car);
                }

                return Unspecified.Instance;
            });

        // score_items: score_items $@7 lilypond_header { $$ = $1; } — $1 is the
        // (possibly reassigned) accumulator; lilypond_header already closed the
        // scope it opened.
        table.Add(
            "score_items: score_items $@7 lilypond_header",
            (context, values, locations, location) => values[0]);
    }
}
