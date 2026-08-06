// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The translation layer: dispatchers, contexts, translator groups and the engraver
/// announce/acknowledge protocol.
/// <para>
/// The behaviour worth pinning here is the part that is easy to get subtly wrong and
/// impossible to notice afterwards: an event must reach a chained dispatcher exactly
/// ONCE even when it matches several classes, and property lookup must walk up the
/// context tree rather than down it.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class TranslationTests
{
    private static Symbol Sym(string name) => Symbol.Intern(name);

    private static StreamEvent Event(params string[] classes)
    {
        List<object> names = new List<object>();
        foreach (string name in classes)
        {
            names.Add(Sym(name));
        }

        return new StreamEvent(Pair.ListFrom(names), Nil.Instance);
    }

    private sealed class RecordingEngraver : Engraver
    {
        public RecordingEngraver(Context context)
            : base(context)
        {
        }

        public override string ClassName => "Recording_engraver";

        public List<string> Phases { get; } = new List<string>();

        public List<Grob> Acknowledged { get; } = new List<Grob>();

        public List<Grob> AcknowledgedEnds { get; } = new List<Grob>();

        public override void StartTranslationTimestep() => Phases.Add("start");

        public override void PreProcessMusic() => Phases.Add("pre");

        public override void ProcessMusic() => Phases.Add("process");

        public override void ProcessAcknowledged() => Phases.Add("acknowledged");

        public override void StopTranslationTimestep() => Phases.Add("stop");

        public override void AcknowledgeGrob(GrobInfo info) => Acknowledged.Add(info.Grob);

        public override void AcknowledgeEndGrob(GrobInfo info) => AcknowledgedEnds.Add(info.Grob);
    }

    private sealed class LastEngraver : Engraver
    {
        public LastEngraver(Context context)
            : base(context)
        {
        }

        public override bool MustBeLast => true;
    }

    [Fact]
    public void a_listener_hears_events_of_the_class_it_registered_for()
    {
        //Arrange
        Dispatcher dispatcher = new Dispatcher();
        List<StreamEvent> heard = new List<StreamEvent>();
        dispatcher.AddListener(this, heard.Add, Sym("note-event"));

        //Act
        dispatcher.Broadcast(Event("note-event", "rhythmic-event", "event"));
        dispatcher.Broadcast(Event("rest-event", "event"));

        //Assert
        heard.Count.Should().Be(1);
    }

    [Fact]
    public void an_event_matching_two_listened_classes_reaches_each_listener_once()
    {
        //Arrange
        // Two DIFFERENT listeners both fire; the dedup rule is about priority, not
        // about class count.
        Dispatcher dispatcher = new Dispatcher();
        int noteHits = 0;
        int rhythmicHits = 0;
        dispatcher.AddListener(this, _ => noteHits++, Sym("note-event"));
        dispatcher.AddListener(this, _ => rhythmicHits++, Sym("rhythmic-event"));

        //Act
        dispatcher.Broadcast(Event("note-event", "rhythmic-event", "event"));

        //Assert
        noteHits.Should().Be(1);
        rhythmicHits.Should().Be(1);
    }

    [Fact]
    public void listeners_are_called_in_registration_order()
    {
        //Arrange
        // Priority is assigned on registration and listeners run low priority first.
        Dispatcher dispatcher = new Dispatcher();
        List<string> order = new List<string>();
        dispatcher.AddListener(this, _ => order.Add("first"), Sym("note-event"));
        dispatcher.AddListener(this, _ => order.Add("second"), Sym("note-event"));

        //Act
        dispatcher.Broadcast(Event("note-event"));

        //Assert
        order.Should().Equal(new List<string> { "first", "second" });
    }

    [Fact]
    public void a_chained_dispatcher_receives_a_multi_class_event_only_once()
    {
        //Arrange
        // THE reason the priority mechanism exists. The downstream dispatcher listens
        // for two classes; an event carrying both must not be forwarded twice.
        Dispatcher upstream = new Dispatcher();
        Dispatcher downstream = new Dispatcher();

        int hits = 0;
        downstream.AddListener(this, _ => hits++, Sym("note-event"));
        downstream.AddListener(this, _ => hits++, Sym("rhythmic-event"));
        downstream.RegisterAsListener(upstream);

        //Act
        upstream.Broadcast(Event("note-event", "rhythmic-event", "event"));

        //Assert
        // Each of the two listeners fires once. Had the forwarding itself been
        // duplicated, this would be four.
        hits.Should().Be(2);
    }

    [Fact]
    public void unregistering_a_chained_dispatcher_stops_the_forwarding()
    {
        //Arrange
        Dispatcher upstream = new Dispatcher();
        Dispatcher downstream = new Dispatcher();
        int hits = 0;
        downstream.AddListener(this, _ => hits++, Sym("note-event"));
        downstream.RegisterAsListener(upstream);

        //Act
        downstream.UnregisterAsListener(upstream);
        upstream.Broadcast(Event("note-event"));

        //Assert
        hits.Should().Be(0);
    }

    [Fact]
    public void a_removed_listener_stops_hearing_events()
    {
        //Arrange
        Dispatcher dispatcher = new Dispatcher();
        int hits = 0;
        Listener listener = dispatcher.AddListener(this, _ => hits++, Sym("note-event"));

        //Act
        dispatcher.RemoveListener(listener, Sym("note-event"));
        dispatcher.Broadcast(Event("note-event"));

        //Assert
        hits.Should().Be(0);
        dispatcher.ListenedTypes.Should().BeEmpty();
    }

    [Fact]
    public void a_dispatcher_reports_which_classes_are_listened_to()
    {
        //Arrange
        Dispatcher dispatcher = new Dispatcher();
        dispatcher.AddListener(this, _ => { }, Sym("note-event"));

        //Act
        bool listened = dispatcher.IsListenedClass(Pair.List(Sym("note-event"), Sym("event")));

        //Assert
        listened.Should().BeTrue();
        dispatcher.IsListenedClass(Pair.List(Sym("rest-event"))).Should().BeFalse();
    }

    [Fact]
    public void a_context_property_is_found_by_walking_up_the_tree()
    {
        //Arrange
        Context score = new Context(Sym("Score"));
        Context staff = new Context(Sym("Staff"));
        Context voice = new Context(Sym("Voice"));
        score.AddContext(staff);
        staff.AddContext(voice);

        score.SetProperty("fontSize", 3L);

        //Act
        object found = voice.GetProperty("fontSize");

        //Assert
        found.Should().Be(3L);
        voice.WhereDefined(Sym("fontSize"), out _).Should().BeSameAs(score);
    }

    [Fact]
    public void a_nearer_context_shadows_a_further_one()
    {
        //Arrange
        Context score = new Context(Sym("Score"));
        Context staff = new Context(Sym("Staff"));
        score.AddContext(staff);
        score.SetProperty("fontSize", 3L);
        staff.SetProperty("fontSize", 7L);

        //Act
        object found = staff.GetProperty("fontSize");

        //Assert
        found.Should().Be(7L);
        score.GetProperty("fontSize").Should().Be(3L);
    }

    [Fact]
    public void an_unset_context_property_reads_as_the_empty_list()
    {
        //Arrange
        Context score = new Context(Sym("Score"));

        //Act
        object value = score.GetProperty("noSuchProperty");

        //Assert
        value.Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void here_defined_does_not_walk_up_the_tree()
    {
        //Arrange
        Context score = new Context(Sym("Score"));
        Context staff = new Context(Sym("Staff"));
        score.AddContext(staff);
        score.SetProperty("fontSize", 3L);

        //Act
        bool here = staff.HereDefined(Sym("fontSize"), out _);

        //Assert
        here.Should().BeFalse();
        score.HereDefined(Sym("fontSize"), out _).Should().BeTrue();
    }

    [Fact]
    public void a_context_answers_to_its_own_name_and_its_aliases()
    {
        //Arrange
        Context voice = new Context(Sym("Voice"));
        voice.AddAlias(Sym("Bottom"));

        //Act
        bool isVoice = voice.IsAlias(Sym("Voice"));

        //Assert
        isVoice.Should().BeTrue();
        voice.IsAlias(Sym("Staff")).Should().BeFalse();
    }

    [Fact]
    public void bottom_matches_any_context_that_accepts_no_children()
    {
        //Arrange
        // "Bottom" means ACCEPTS no children, not HAS no children -- upstream's test is
        // !acceptance_.has_default (), and it says nothing about whether children exist.
        // So the Staff has to declare that it accepts Voice; merely giving it a Voice
        // child does not make it a non-bottom context, and a fixture that only adds the
        // child is testing the wrong thing.
        Context staff = new Context(Sym("Staff"));
        Context voice = new Context(Sym("Voice"));
        staff.Acceptance.AcceptDefault(Sym("Voice"));
        staff.AddContext(voice);

        //Act
        bool voiceIsBottom = voice.IsAlias(Sym("Bottom"));

        //Assert
        voiceIsBottom.Should().BeTrue();
        staff.IsAlias(Sym("Bottom")).Should().BeFalse();
    }

    [Fact]
    public void finding_a_context_searches_downward_then_outward()
    {
        //Arrange
        Context score = new Context(Sym("Score"));
        Context staffOne = new Context(Sym("Staff"), "one");
        Context staffTwo = new Context(Sym("Staff"), "two");
        Context voice = new Context(Sym("Voice"));
        score.AddContext(staffOne);
        score.AddContext(staffTwo);
        staffOne.AddContext(voice);

        //Act
        Context found = voice.FindContext(Sym("Staff"), "two");

        //Assert
        found.Should().BeSameAs(staffTwo);
        voice.FindContext(Sym("Staff"), "one").Should().BeSameAs(staffOne);
        voice.FindContext(Sym("Score")).Should().BeSameAs(score);
        voice.FindContext(Sym("Lyrics")).Should().BeNull();
    }

    [Fact]
    public void adding_a_child_context_chains_its_event_source_to_the_parent()
    {
        //Arrange
        // This is the chain that lets a Score-level engraver hear a Voice-level note.
        Context score = new Context(Sym("Score"));
        Context voice = new Context(Sym("Voice"));
        score.AddContext(voice);

        int scoreHits = 0;
        score.EventsBelow.AddListener(this, _ => scoreHits++, Sym("note-event"));

        //Act
        voice.EventsBelow.Broadcast(Event("note-event"));

        //Assert
        scoreHits.Should().Be(1);
    }

    [Fact]
    public void a_translator_group_runs_every_phase_in_order()
    {
        //Arrange
        Context voice = new Context(Sym("Voice"));
        EngraverGroup group = new EngraverGroup();
        voice.Implementation = group;
        group.ConnectToContext(voice);

        RecordingEngraver engraver = new RecordingEngraver(voice);
        group.AddTranslator(engraver);

        //Act
        group.RunPhase(TranslatorPrecomputeIndex.StartTranslationTimestep);
        group.RunPhase(TranslatorPrecomputeIndex.PreProcessMusic);
        group.RunPhase(TranslatorPrecomputeIndex.ProcessMusic);
        group.RunPhase(TranslatorPrecomputeIndex.ProcessAcknowledged);
        group.RunPhase(TranslatorPrecomputeIndex.StopTranslationTimestep);

        //Assert
        engraver.Phases.Should().Equal(new List<string>
        {
            "start",
            "pre",
            "process",
            "acknowledged",
            "stop",
        });
    }

    [Fact]
    public void a_translator_that_must_be_last_stays_last()
    {
        //Arrange
        Context voice = new Context(Sym("Voice"));
        EngraverGroup group = new EngraverGroup();
        voice.Implementation = group;

        LastEngraver last = new LastEngraver(voice);
        RecordingEngraver ordinary = new RecordingEngraver(voice);

        //Act
        group.AddTranslator(last);
        group.AddTranslator(ordinary);

        //Assert
        group.Translators[0].Should().BeSameAs(ordinary);
        group.Translators[1].Should().BeSameAs(last);
    }

    [Fact]
    public void recursing_downward_runs_a_parent_before_its_children()
    {
        //Arrange
        Context score = new Context(Sym("Score"));
        Context voice = new Context(Sym("Voice"));
        score.AddContext(voice);

        List<string> order = new List<string>();
        score.Implementation = MakeGroup(score, order, "score");
        voice.Implementation = MakeGroup(voice, order, "voice");

        //Act
        TranslatorGroup.RecurseOverTranslators(
            score,
            TranslatorPrecomputeIndex.ProcessMusic,
            Direction.Negative);

        //Assert
        order.Should().Equal(new List<string> { "score", "voice" });
    }

    [Fact]
    public void recursing_upward_runs_children_before_their_parent()
    {
        //Arrange
        Context score = new Context(Sym("Score"));
        Context voice = new Context(Sym("Voice"));
        score.AddContext(voice);

        List<string> order = new List<string>();
        score.Implementation = MakeGroup(score, order, "score");
        voice.Implementation = MakeGroup(voice, order, "voice");

        //Act
        TranslatorGroup.RecurseOverTranslators(
            score,
            TranslatorPrecomputeIndex.ProcessMusic,
            Direction.Positive);

        //Assert
        order.Should().Equal(new List<string> { "voice", "score" });
    }

    [Fact]
    public void an_engraver_hears_a_stream_event_it_listened_for()
    {
        //Arrange
        Context voice = new Context(Sym("Voice"));
        EngraverGroup group = new EngraverGroup();
        voice.Implementation = group;
        group.ConnectToContext(voice);

        List<StreamEvent> heard = new List<StreamEvent>();
        ListeningEngraver engraver = new ListeningEngraver(voice, heard);
        group.AddTranslator(engraver);
        engraver.ConnectToContext();

        //Act
        voice.EventSource.Broadcast(Event("note-event", "event"));

        //Assert
        heard.Count.Should().Be(1);
    }

    [Fact]
    public void an_announced_grob_is_acknowledged_by_the_other_engravers()
    {
        //Arrange
        Context voice = new Context(Sym("Voice"));
        EngraverGroup group = new EngraverGroup();
        voice.Implementation = group;
        group.ConnectToContext(voice);

        RecordingEngraver maker = new RecordingEngraver(voice);
        RecordingEngraver watcher = new RecordingEngraver(voice);
        group.AddTranslator(maker);
        group.AddTranslator(watcher);

        Item grob = new Item(Pair.List(new Pair(Sym("meta"), Nil.Instance)));

        //Act
        maker.AnnounceGrob(grob, null);
        group.AcknowledgeGrobs();

        //Assert
        watcher.Acknowledged.Should().Contain(grob);

        // An engraver never acknowledges what it made itself.
        maker.Acknowledged.Should().BeEmpty();
    }

    [Fact]
    public void an_announcement_travels_up_to_the_parent_context()
    {
        //Arrange
        // A Staff-level engraver has to see grobs made in a Voice below it — that is
        // how, for instance, a Staff-level engraver collects note heads.
        Context staff = new Context(Sym("Staff"));
        Context voice = new Context(Sym("Voice"));
        staff.AddContext(voice);

        EngraverGroup staffGroup = new EngraverGroup();
        EngraverGroup voiceGroup = new EngraverGroup();
        staff.Implementation = staffGroup;
        voice.Implementation = voiceGroup;
        staffGroup.ConnectToContext(staff);
        voiceGroup.ConnectToContext(voice);

        RecordingEngraver staffWatcher = new RecordingEngraver(staff);
        RecordingEngraver voiceMaker = new RecordingEngraver(voice);
        staffGroup.AddTranslator(staffWatcher);
        voiceGroup.AddTranslator(voiceMaker);

        Item grob = new Item(Pair.List(new Pair(Sym("meta"), Nil.Instance)));

        //Act
        voiceMaker.AnnounceGrob(grob, null);
        staffGroup.AcknowledgeGrobs();

        //Assert
        staffWatcher.Acknowledged.Should().Contain(grob);
    }

    [Fact]
    public void an_end_announcement_is_acknowledged_separately()
    {
        //Arrange
        Context voice = new Context(Sym("Voice"));
        EngraverGroup group = new EngraverGroup();
        voice.Implementation = group;
        group.ConnectToContext(voice);

        RecordingEngraver maker = new RecordingEngraver(voice);
        RecordingEngraver watcher = new RecordingEngraver(voice);
        group.AddTranslator(maker);
        group.AddTranslator(watcher);

        Spanner spanner = new Spanner(Pair.List(new Pair(Sym("meta"), Nil.Instance)));

        //Act
        maker.AnnounceEndGrob(spanner, null);
        group.AcknowledgeGrobs();

        //Assert
        watcher.AcknowledgedEnds.Should().Contain(spanner);
        watcher.Acknowledged.Should().BeEmpty();
    }

    [Fact]
    public void acknowledging_clears_the_queue()
    {
        //Arrange
        Context voice = new Context(Sym("Voice"));
        EngraverGroup group = new EngraverGroup();
        voice.Implementation = group;
        group.ConnectToContext(voice);

        RecordingEngraver maker = new RecordingEngraver(voice);
        RecordingEngraver watcher = new RecordingEngraver(voice);
        group.AddTranslator(maker);
        group.AddTranslator(watcher);

        maker.AnnounceGrob(new Item(Pair.List(new Pair(Sym("meta"), Nil.Instance))), null);

        //Act
        group.AcknowledgeGrobs();
        group.AcknowledgeGrobs();

        //Assert
        watcher.Acknowledged.Count.Should().Be(1);
        group.AnnounceInfos.Should().BeEmpty();
    }

    [Fact]
    public void making_a_grob_records_the_event_that_caused_it()
    {
        //Arrange
        Context voice = new Context(Sym("Voice"));
        EngraverGroup group = new EngraverGroup();
        voice.Implementation = group;
        group.ConnectToContext(voice);

        DefinitionEngraver engraver = new DefinitionEngraver(voice);
        group.AddTranslator(engraver);

        StreamEvent cause = Event("note-event", "event");

        //Act
        Item head = engraver.MakeItem("NoteHead", cause);

        //Assert
        head.Should().NotBeNull();
        head.Name.Should().Be("NoteHead");
        head.GetProperty("cause").Should().BeSameAs(cause);
        head.HasInterface("note-head-interface").Should().BeTrue();
    }

    private sealed class ListeningEngraver : Engraver
    {
        private readonly List<StreamEvent> _heard;

        public ListeningEngraver(Context context, List<StreamEvent> heard)
            : base(context) => _heard = heard;

        public override void ConnectToContext() => ListenTo("note-event", _heard.Add);
    }

    /// <summary>An engraver with a hand-supplied grob definition table.</summary>
    private sealed class DefinitionEngraver : Engraver
    {
        public DefinitionEngraver(Context context)
            : base(context)
        {
        }

        protected override object LookupGrobDefinition(Symbol grobName)
        {
            object meta = Pair.List(
                new Pair(Sym("name"), grobName),
                new Pair(Sym("classes"), Pair.List(Sym("Item"))),
                new Pair(Sym("interfaces"), Pair.List(Sym("note-head-interface"))));

            return Pair.List(new Pair(Sym("meta"), meta));
        }
    }

    private sealed class OrderingEngraver : Engraver
    {
        private readonly List<string> _order;
        private readonly string _label;

        public OrderingEngraver(Context context, List<string> order, string label)
            : base(context)
        {
            _order = order;
            _label = label;
        }

        public override void ProcessMusic() => _order.Add(_label);
    }

    private static EngraverGroup MakeGroup(Context context, List<string> order, string label)
    {
        EngraverGroup group = new EngraverGroup();
        group.ConnectToContext(context);
        group.AddTranslator(new OrderingEngraver(context, order, label));
        return group;
    }
}
