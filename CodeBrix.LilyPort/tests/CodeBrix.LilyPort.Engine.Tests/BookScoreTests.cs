// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The <see cref="Book"/> and <see cref="Score"/> objects, ported for the parser's
/// book/bookpart/score rule actions (RAG3): construction defaults, the
/// reverse-order score list, <c>set_music</c>'s error handling, and the
/// <c>ly:book?</c>/<c>ly:score?</c> predicates now that instances can exist.
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class BookScoreTests
{
    [Fact]
    public void a_new_book_is_empty_with_no_paper()
    {
        //Arrange & Act
        Book book = new Book();

        //Assert
        book.Paper.Should().BeNull();
        book.Header.Should().BeSameAs(Nil.Instance);
        book.Scores.Should().BeSameAs(Nil.Instance);
        book.Bookparts.Should().BeSameAs(Nil.Instance);
        book.Origin.Should().BeNull();
    }

    [Fact]
    public void add_score_conses_to_the_front_so_the_list_is_in_reverse_order()
    {
        //Arrange
        Book book = new Book();
        Score first = new Score();
        Score second = new Score();

        //Act
        book.AddScore(first);
        book.AddScore(second);

        //Assert
        Pair scores = (Pair)book.Scores;
        scores.Car.Should().BeSameAs(second);
        ((Pair)scores.Cdr).Car.Should().BeSameAs(first);
        ((Pair)scores.Cdr).Cdr.Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void set_spot_records_the_origin()
    {
        //Arrange
        Book book = new Book();
        Score score = new Score();
        object location = "somewhere.ly:3";

        //Act
        book.SetSpot(location);
        score.SetSpot(location);

        //Assert
        book.Origin.Should().BeSameAs(location);
        score.Origin.Should().BeSameAs(location);
    }

    [Fact]
    public void a_new_score_has_no_music_no_header_and_no_error()
    {
        //Arrange & Act
        Score score = new Score();

        //Assert
        score.GetMusic().Should().BeSameAs(Nil.Instance);
        score.GetHeader().Should().BeSameAs(Nil.Instance);
        score.ErrorFound.Should().BeFalse();
        score.Defs.Should().BeEmpty();
    }

    [Fact]
    public void set_music_stores_the_music()
    {
        //Arrange
        Score score = new Score();
        MusicObject music = new MusicObject(Nil.Instance);

        //Act
        score.SetMusic(music);

        //Assert
        score.GetMusic().Should().BeSameAs(music);
        score.ErrorFound.Should().BeFalse();
    }

    [Fact]
    public void set_music_with_error_found_music_marks_the_score_and_drops_the_music()
    {
        //Arrange
        // Only exactly #t counts: upstream's from_scm<bool> is scm_is_eq with
        // SCM_BOOL_T, so an unset property (the empty list) must NOT trip this.
        Score score = new Score();
        MusicObject music = new MusicObject(Nil.Instance);
        music.SetProperty("error-found", true);

        //Act
        score.SetMusic(music);

        //Assert
        score.ErrorFound.Should().BeTrue();
        score.GetMusic().Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void set_music_twice_complains_but_keeps_the_new_music()
    {
        //Arrange
        Score score = new Score();
        MusicObject first = new MusicObject(Nil.Instance);
        MusicObject second = new MusicObject(Nil.Instance);
        score.SetMusic(first);

        bool recorded = Warn.RecordMessages;
        Warn.RecordMessages = true;
        Warn.ClearMessages();
        try
        {
            //Act
            score.SetMusic(second);

            //Assert
            score.GetMusic().Should().BeSameAs(second);
            Warn.Messages.Should().HaveCount(2);
            Warn.Messages[0].Should().Contain("already have music in score");
            Warn.Messages[1].Should().Contain("this is the previous music");
        }
        finally
        {
            Warn.ClearMessages();
            Warn.RecordMessages = recorded;
        }
    }

    [Fact]
    public void add_output_def_appends_in_order()
    {
        //Arrange
        Score score = new Score();
        OutputDef layout = new OutputDef();
        OutputDef midi = new OutputDef();

        //Act
        score.AddOutputDef(layout);
        score.AddOutputDef(midi);

        //Assert
        score.Defs.Should().Equal(layout, midi);
    }

    [Fact]
    public void set_header_get_header_round_trip()
    {
        //Arrange
        Score score = new Score();
        object module = new object();

        //Act
        score.SetHeader(module);

        //Assert
        score.GetHeader().Should().BeSameAs(module);
    }

    [Fact]
    public void the_book_and_score_predicates_answer_over_real_instances()
    {
        //Arrange
        // The standing obligation from TypePredicates: a stub answering #f was
        // correct while no Book or Score could exist, and would be silently wrong
        // now that they can.
        string result = null;

        //Act
        // CreateInterpreter publishes the bare interpreter as the ambient one;
        // restore whatever was ambient before, or every later context-property
        // assignment in the process would type-check against its empty tables.
        Interpreter ambientBefore = LilyPondScheme.Current;
        try
        {
            Interpreter.RunWithLargeStack(() =>
            {
                Interpreter interpreter = LilyPondScheme.CreateInterpreter();
                interpreter.CurrentModule.Define(Symbol.Intern("the-book"), new Book());
                interpreter.CurrentModule.Define(Symbol.Intern("the-score"), new Score());
                result = Printer.Write(interpreter.EvalString(
                    "(list (ly:book? the-book)"
                    + "     (ly:score? the-score)"
                    + "     (ly:book? the-score)"
                    + "     (ly:score? the-book)"
                    + "     (ly:book? 42)"
                    + "     (ly:score? 42))",
                    "<test>"));
            });
        }
        finally
        {
            LilyPondScheme.RestoreAmbient(ambientBefore);
        }

        //Assert
        result.Should().Be("(#t #t #f #f #f #f)");
    }
}
