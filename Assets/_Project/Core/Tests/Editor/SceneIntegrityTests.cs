using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PoOlympic.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace PoOlympic.Tests
{
    /// <summary>
    /// Every scene of the player build is whole: no missing scripts, no reference to an object that is gone, the UI
    /// bound to what it needs (HUD → board + style sheet, menu → its UXML and loadable event scenes), and no IMGUI
    /// (OnGUI) screen — those are dev screens and stay out of the build (EventScenes.SetBuildScenes).
    /// </summary>
    public class SceneIntegrityTests
    {
        [Serializable]
        class EventCatalog
        {
            public CatalogEvent[] events = Array.Empty<CatalogEvent>();
        }

        [Serializable]
        class CatalogEvent
        {
            public int number = 0;
            public string name = "";
        }

        public static IEnumerable<string> BuildScenes() => EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path);

        string _before;

        [OneTimeSetUp] public void Remember() => _before = EditorSceneManager.GetActiveScene().path;

        [OneTimeTearDown]
        public void Restore()
        {
            if (!string.IsNullOrEmpty(_before) && File.Exists(_before)) EditorSceneManager.OpenScene(_before, OpenSceneMode.Single);
        }

        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        [Test]
        public void MenuIsTheStartScene() =>
            Assert.AreEqual(MeetLineup.MenuScene, Path.GetFileNameWithoutExtension(BuildScenes().FirstOrDefault() ?? ""));

        [Test]
        public void EventCatalogueAndPlayableScenesUseTheRetainedRoster()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(EventScenes.CatalogJson);
            Assert.NotNull(asset);
            var events = JsonUtility.FromJson<EventCatalog>(asset.text).events;
            Assert.NotNull(events);

            var retained = new[] { 1, 5, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 19, 20, 21, 27 };
            Assert.AreEqual(EventScenes.CatalogEventCount, events.Length);
            CollectionAssert.AreEqual(retained, events.Select(e => e.number).ToArray());
            Assert.AreEqual("Crab Shuffle Relay", events.Single(e => e.number == 10).name);

            var playable = new[] { 1, 5, 8, 9, 10, 11, 12, 13, 14, 19, 27 };
            CollectionAssert.AreEqual(playable, EventScenes.EventScenePaths.Keys.OrderBy(n => n).ToArray());
        }

        [Test]
        public void GeneratedMenuAndHubContainOnlyTheRetainedRoster()
        {
            var menuScene = EditorSceneManager.OpenScene(EventScenes.MainMenuScene, OpenSceneMode.Single);
            var menu = menuScene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MainMenuController>(true))
                .Single();
            menu.Populate();
            CollectionAssert.AreEqual(
                new[] { 1, 5, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 19, 20, 21, 27 },
                menu.events.Select(e => e.number).OrderBy(n => n));
            CollectionAssert.AreEqual(Enumerable.Range(1, EventScenes.CatalogEventCount),
                menu.events.Select(e => e.displayNumber));
            Assert.IsTrue(menu.events.All(e => e.participants.Length == EventCompetition.EntrantCount &&
                e.participants.All(MeetLineup.Roster.Contains)));
            foreach (var e in menu.events)
            {
                for (int heat = 0; heat < EventCompetition.HeatCount; heat++)
                    Assert.AreEqual(MeetLineup.Roster.Length,
                        EventCompetition.ParticipantsForHeat(e.participants, heat).Distinct().Count(),
                        $"Event {e.displayNumber:00}, heat {heat + 1} must retain a random draw from every current agent.");
                if (EventScenes.EventScenePaths.TryGetValue(e.number, out var scenePath))
                    Assert.AreEqual(Path.GetFileNameWithoutExtension(scenePath), e.scene);
                else
                    Assert.IsEmpty(e.scene, $"Event {e.number:00} must remain visibly unavailable until its scene is built.");
            }
            var eventButtons = menu.GetComponent<UIDocument>().rootVisualElement
                .Query<UnityEngine.UIElements.Button>(className: "event-button")
                .ToList();
            Assert.AreEqual(EventScenes.CatalogEventCount, eventButtons.Count);
            for (int i = 0; i < menu.events.Count; i++)
            {
                bool playable = EventScenes.EventScenePaths.ContainsKey(menu.events[i].number);
                Assert.AreEqual(playable, eventButtons[i].enabledSelf, $"Event {menu.events[i].number:00} enabled state");
                Assert.AreEqual($"{i + 1:00}", eventButtons[i].Q<UnityEngine.UIElements.Label>(className: "event-number").text);
                var mark = eventButtons[i].Q<UnityEngine.UIElements.Label>(className: "event-mark");
                Assert.AreEqual(!playable, mark.ClassListContains("event-mark--unavailable"),
                    $"Event {menu.events[i].number:00} availability marker");
                if (!playable) Assert.AreEqual("SOON", mark.text, $"Event {menu.events[i].number:00} tile status");
            }

            var hubScene = EditorSceneManager.OpenScene(EventScenes.HubScene, OpenSceneMode.Single);
            var venues = hubScene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<EventVenue>(true))
                .OrderBy(v => v.number)
                .ToArray();
            Assert.AreEqual(EventScenes.CatalogEventCount, venues.Length);
            CollectionAssert.AreEqual(
                new[] { 1, 5, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 19, 20, 21, 27 },
                venues.Select(v => v.number));
            Assert.AreEqual(11, venues.Count(v => v.Playable));
        }

        [TestCaseSource(nameof(BuildScenes))]
        public void BuildSceneIsWhole(string path)
        {
            Assert.IsTrue(File.Exists(path), "in Build Settings but not on disk");
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var built = new HashSet<string>(BuildScenes().Select(Path.GetFileNameWithoutExtension));
            var bad = new List<string>();
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var t in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)))
            {
                int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                if (missing > 0) bad.Add($"{missing} missing script(s): {PathOf(t)}");
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c == null) continue;
                    var type = c.GetType();
                    string where = $"{PathOf(t)} ({type.Name})";
                    if (type.Namespace == "PoOlympic" && type.GetMethod("OnGUI", any) != null) bad.Add($"IMGUI screen in a build scene: {where}");

                    // a reference that points at an object which no longer exists (not one left empty on purpose)
                    using var so = new SerializedObject(c);
                    for (var p = so.GetIterator(); p.NextVisible(true);)
                        if (p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue == null && p.objectReferenceEntityIdValue != EntityId.None)
                            bad.Add($"broken reference: {where}.{p.propertyPath}");

                    switch (c)
                    {
                        case UIDocument doc when doc.panelSettings == null: bad.Add($"UIDocument without panel settings: {where}"); break;
                        case BroadcastHud hud:
                            if (!(hud.board is IBroadcastBoard)) bad.Add($"HUD without an event board: {where}");
                            if (hud.style == null) bad.Add($"HUD without its style sheet: {where}");
                            break;
                        case PerfOverlay perf when perf.style == null: bad.Add($"PerfOverlay without its style sheet: {where}"); break;
                        case MainMenuController menu:
                            if (menu.GetComponent<UIDocument>().visualTreeAsset == null) bad.Add($"menu without its UXML: {where}");
                            if (menu.events.Count == 0) bad.Add($"menu without events: {where}");
                            foreach (var e in menu.events.Where(e => !string.IsNullOrEmpty(e.scene) && !built.Contains(e.scene)))
                                bad.Add($"menu event {e.number:00} loads '{e.scene}', which is not in Build Settings");
                            break;
                    }
                }
            }
            Assert.IsEmpty(bad, $"{path}:\n  " + string.Join("\n  ", bad.Take(30)));
        }
    }
}
