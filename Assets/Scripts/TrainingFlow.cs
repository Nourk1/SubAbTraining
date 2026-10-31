using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;
using UnityEngine.InputSystem;

namespace RealtimePatient
{
    /// <summary>Training screens and chapter-specific voice practice.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class TrainingFlow : MonoBehaviour
    {
        private VisualElement root;
        private PanelSettings panel;
        private PatientPreview patientPreview;
        [SerializeField, Tooltip("Optional replacement guide prefab. Leave empty to use the temporary nurse.")]
        private GameObject menuGuidePrefab;
        private MenuGuidePresenter menuGuide;
        private TrainingConversation conversation;
        private Label voiceStatus;
        private Label patientCaption;
        private Button connectButton;
        private Button speakButton;
        private Button pauseButton;
        private bool sessionVisible;
        private VisualElement navigationPrompt;
        private System.Action pendingNavigation;
        private Label objectiveStatus;
        private Label coachingText;
        private Label coachingStatus;
        private VisualElement assessmentView;
        private int assessmentRevision = -1;
        private Label saveStatus;
        private VisualElement objectiveReview;
        private int objectiveRevision = -1;
        private readonly System.Collections.Generic.Dictionary<string, Label> objectiveIndicators = new System.Collections.Generic.Dictionary<string, Label>();
        private readonly System.Collections.Generic.Dictionary<string, Label> objectiveLabels = new System.Collections.Generic.Dictionary<string, Label>();
        private int selected;
        private string difficulty = "Beginner";
        private bool captions = true;
        private ScenarioCatalog catalog;
        private TrainingScenario Chapter => catalog.chapters[selected];
        private string DisplaySpeaker(string speaker) => speaker == "Patient" ? Chapter.ParticipantLabel : speaker;
        private readonly Color ink = new Color(0.09f, 0.19f, 0.25f);
        private readonly Color teal = new Color(0.03f, 0.40f, 0.42f);

        private void Awake()
        {
            // Voice starts only after the learner selects Connect Patient.
            var client = FindFirstObjectByType<OpenAIRealtimeClient>();
            if (client != null) client.ConnectOnStart = false;
            var microphone = FindFirstObjectByType<CtrlPushToTalk>();
            if (microphone != null) microphone.enabled = false;
            conversation = gameObject.AddComponent<TrainingConversation>();
        }

        private System.Collections.IEnumerator Start()
        {
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("TrainingTheme");
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1440, 900);
            panel.sortingOrder = 100;
            var document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panel;
            // UIDocument can rebuild its root during its first enable/update.
            // Populate the final tree after that initial lifecycle has completed.
            yield return null;
            root = document.rootVisualElement;
            var menuStyles = Resources.Load<StyleSheet>("MenuNavigation");
            if (menuStyles != null) root.styleSheets.Add(menuStyles);
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.right = 0;
            root.style.top = 0;
            root.style.bottom = 0;
            root.style.flexGrow = 1;
            root.style.backgroundColor = new Color(0.94f, 0.97f, 0.97f);
            root.style.color = ink;
            root.style.fontSize = 18;
            catalog = ScenarioCatalog.Load();
            Menu();
        }

        private void Page(string title, string subtitle, bool scrollable = true, bool showHeader = true)
        {
            CloseNavigationPrompt();
            sessionVisible = false;
            pauseButton = null;
            if (conversation != null) conversation.Stop();
            voiceStatus = patientCaption = null;
            connectButton = speakButton = null;
            objectiveStatus = null; objectiveReview = null; objectiveRevision = -1;
            coachingText = coachingStatus = null;
            assessmentView = null; assessmentRevision = -1;
            saveStatus = null;
            objectiveIndicators.Clear(); objectiveLabels.Clear();
            if (patientPreview != null) patientPreview.Hide();
            if (menuGuide != null) menuGuide.Hide();
            root.Clear();
            root.style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>("MenuCorridorBackground"));
            root.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            root.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            root.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            root.style.backgroundColor = Color.white;
            var view = new VisualElement();
            view.style.flexGrow = 1; view.style.minHeight = 0;
            if (showHeader)
            {
                view.AddToClassList("menu-view");
            }
            root.Add(view);
            if (showHeader)
            {
                var header = new VisualElement();
                header.style.backgroundColor = Color.clear;
                header.style.paddingLeft = 36; header.style.paddingTop = 20;
                header.style.paddingBottom = 20;
                var brand = new Label("PATRICIA  /  PATIENT COMMUNICATION TRAINING");
                brand.style.color = ink;
                brand.style.unityFontStyleAndWeight = FontStyle.Bold;
                header.Add(brand); view.Add(header);
            }
            VisualElement body = scrollable ? new ScrollView() : new VisualElement();
            if (body is ScrollView menuScroll) ConfigureMenuScroll(menuScroll);
            body.style.flexGrow = 1;
            body.style.minHeight = 0;
            body.style.paddingLeft = 36; body.style.paddingRight = 36;
            body.style.paddingTop = 28; body.style.paddingBottom = 28;
            view.Add(body);
            content = body.contentContainer;
            if (!scrollable) content.style.flexGrow = 1;
            if (showHeader)
            {
                Text(content, title, 34, true);
                Text(content, subtitle, 18);
            }
        }
        private VisualElement content;
        private void ConfigureMenuScroll(ScrollView scroll)
        {
            scroll.AddToClassList("menu-scroll");
            scroll.mode = ScrollViewMode.Vertical;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            void RefreshVisibleText()
            {
                var viewport = scroll.contentViewport.worldBound;
                if (viewport.width <= 0 || viewport.height <= 0) return;
                scroll.contentContainer.Query<Label>(className: "scroll-reveal-text").ForEach(label =>
                {
                    // Nested scroll views manage their own content independently.
                    if (label.GetFirstAncestorOfType<ScrollView>() != scroll) return;
                    label.AddToClassList("scroll-reveal-managed");
                    bool visible = label.worldBound.Overlaps(viewport);
                    // Opacity preserves layout and leaves scrolling and controls responsive.
                    label.EnableInClassList("scroll-reveal-visible", visible);
                });
            }
            scroll.verticalScroller.valueChanged += _ => RefreshVisibleText();
            scroll.horizontalScroller.valueChanged += _ => RefreshVisibleText();
            scroll.RegisterCallback<GeometryChangedEvent>(_ => RefreshVisibleText());
            // Also discover content added later (saved results, foldouts, and live feedback).
            // UI Toolkit suspends scheduled work when this view is detached.
            scroll.schedule.Execute(RefreshVisibleText).Every(80);
        }

        private void Text(VisualElement parent, string text, int size = 18, bool bold = false)
        {
            var label = new Label(text);
            label.AddToClassList("scroll-reveal-text");
            label.style.fontSize = size;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 14;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            parent.Add(label);
        }
        private VisualElement Card(VisualElement parent)
        {
            var card = new VisualElement();
            card.style.backgroundColor = new Color(1f, 1f, 1f, 0.22f);
            card.style.paddingLeft = 24; card.style.paddingRight = 24;
            card.style.paddingTop = 24; card.style.paddingBottom = 24;
            card.style.marginBottom = 18;
            card.style.borderTopLeftRadius = 14; card.style.borderTopRightRadius = 14;
            card.style.borderBottomLeftRadius = 14; card.style.borderBottomRightRadius = 14;
            parent.Add(card); return card;
        }
        private Button Action(VisualElement parent, string title, System.Action click)
        {
            var button = MenuAction(parent, title, click);
            button.AddToClassList("menu-action-compact");
            return button;
        }
        private void StyleMenuButton(Button button)
        {
            button.AddToClassList("menu-action");
            var depth = new VisualElement { pickingMode = PickingMode.Ignore };
            depth.AddToClassList("menu-action-depth");
            button.Add(depth);
        }
        private Button MenuAction(VisualElement parent, string title, System.Action click)
        {
            var button = new Button(click) { text = title };
            StyleMenuButton(button);
            parent.Add(button);
            return button;
        }
        private void Menu()
        {
            Page(null, null, false, false);
            root.style.backgroundColor = Color.white;
            root.style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>("MenuCorridorBackground"));
            root.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            root.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            root.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            content.style.paddingLeft = 0; content.style.paddingRight = 0;
            content.style.paddingTop = 0; content.style.paddingBottom = 0;
            var guide = new VisualElement();
            guide.style.position = Position.Absolute;
            guide.style.left = 0; guide.style.right = 0; guide.style.top = 0; guide.style.bottom = 0;
            guide.style.flexGrow = 1; guide.style.flexBasis = 0; guide.style.minHeight = 0;
            guide.style.overflow = Overflow.Hidden;
            content.Add(guide);
            if (menuGuide == null) menuGuide = gameObject.AddComponent<MenuGuidePresenter>();
            if (!menuGuide.Show(guide, menuGuidePrefab))
                Debug.LogWarning("Menu guide unavailable. Choose Capstone > Create Menu Guide Assets to prepare it.");
            var navigation = new VisualElement(); navigation.style.flexDirection = FlexDirection.Row;
            navigation.style.position = Position.Absolute;
            navigation.style.left = 36; navigation.style.right = 36; navigation.style.bottom = 28;
            navigation.style.flexShrink = 0; content.Add(navigation);
            var start = MenuAction(navigation, "Start Training", () => { selected = 0; Briefing(); });
            var chapters = MenuAction(navigation, "Select Chapter", Chapters);
            var progress = MenuAction(navigation, "View Progress", Progress);
            var settings = MenuAction(navigation, "Settings", Settings);
            foreach (var button in new[] { start, chapters, progress, settings })
            {
                button.style.flexGrow = 1; button.style.flexBasis = 0; button.style.minWidth = 0;
                button.style.marginLeft = 4; button.style.marginRight = 4;
            }
        }
        private void DifficultySelector(VisualElement parent, System.Action changed = null)
        {
            Text(parent, "Difficulty", 18, true);
            var selector = new DropdownField(new System.Collections.Generic.List<string>
                { "Beginner", "Intermediate", "Advanced" }, difficulty);
            selector.AddToClassList("difficulty-dropdown");
            var input = selector.Q<VisualElement>(className: "unity-base-field__input");
            if (input != null)
            {
                input.AddToClassList("menu-action");
                input.AddToClassList("menu-action-compact");
                var depth = new VisualElement { pickingMode = PickingMode.Ignore };
                depth.AddToClassList("menu-action-depth");
                input.Add(depth);
            }
            selector.RegisterValueChangedCallback(evt =>
            {
                difficulty = evt.newValue;
                changed?.Invoke();
            });
            parent.Add(selector);
        }
        private void Chapters()
        {
            Page("Choose a conversation", "Choose a scenario, review the briefing, and begin voice practice.");
            DifficultySelector(content);
            var grid = new VisualElement(); grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap; content.Add(grid);
            for (int i = 0; i < catalog.chapters.Length; i++)
            {
                int index = i;
                var card = Card(grid); card.style.width = Length.Percent(46);
                card.style.marginRight = 20; card.style.marginTop = 18;
                Text(card, $"CHAPTER {i + 1:00}", 16, true);
                Text(card, catalog.chapters[i].title, 23, true); Text(card, catalog.chapters[i].description);
                Action(card, "View Chapter Briefing", () => { selected = index; Briefing(); });
            }
            Action(content, "Main Menu", Menu);
        }
        private void Briefing()
        {
            Page(Chapter.title, "Fictional scenario draft - awaiting healthcare faculty review");
            var profile = Card(content);
            Text(profile, Chapter.characterName + " | " + Chapter.age + " | " + Chapter.role, 26, true);
            Text(profile, Chapter.setting, 20, true);
            Text(profile, Chapter.briefing);
            if (!string.IsNullOrWhiteSpace(Chapter.PolicyNotice)) Text(profile, Chapter.PolicyNotice, 16, true);
            Text(profile, "Draft scenario. The shared avatar is a visual placeholder and does not yet match every character's age or appearance.", 16);
            Text(profile, "Communication challenge: " + Chapter.initialEmotion);
            var behavior = new Label(ScenarioCatalog.DifficultyBehavior(difficulty));
            DifficultySelector(profile, () => behavior.text = ScenarioCatalog.DifficultyBehavior(difficulty));
            behavior.style.whiteSpace = WhiteSpace.Normal;
            behavior.style.marginTop = 12;
            profile.Add(behavior);
            Text(profile, "Voice practice available. Connect the character on the training screen to begin.", 16);
            var goals = Card(content);
            Text(goals, "Your communication objectives", 23, true);
            foreach (var objective in Chapter.objectives) Text(goals, (Chapter.CanAssess(objective) ? "" : "Pending faculty policy: ") + objective.text);
            Action(content, "Enter Training", Session);
            Action(content, "Chapter Selection", Chapters);
        }

        private void Session()
        {
            Page(null, null, false, false);
            root.style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>("TrainingHospital"));
            root.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            root.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            root.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Bottom);
            content.style.paddingLeft = 0; content.style.paddingRight = 0;
            content.style.paddingTop = 0; content.style.paddingBottom = 0;
            sessionVisible = true;
            conversation.ClearReview();
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
            row.style.flexGrow = 1; row.style.minHeight = 0; content.Add(row);
            row.style.position = Position.Absolute;
            row.style.left = 0; row.style.right = 0; row.style.top = 0; row.style.bottom = 0;
            var patientView = new VisualElement { pickingMode = PickingMode.Ignore };
            patientView.style.position = Position.Absolute;
            patientView.style.left = 0; patientView.style.right = 0;
            patientView.style.top = 0; patientView.style.bottom = 0;
            row.Add(patientView);
            var titleOverlay = new VisualElement { pickingMode = PickingMode.Ignore };
            titleOverlay.style.position = Position.Absolute;
            titleOverlay.style.left = 24; titleOverlay.style.top = 20; titleOverlay.style.right = 360;
            row.Add(titleOverlay);
            var stage = Card(row);
            stage.style.position = Position.Absolute;
            stage.style.left = 24; stage.style.right = 360; stage.style.bottom = 96; stage.style.flexGrow = 1; stage.style.flexBasis = 0;
            stage.style.minWidth = 0;
            stage.style.backgroundColor = Color.clear;
            stage.style.paddingLeft = 0; stage.style.paddingRight = 0;
            stage.style.paddingTop = 0; stage.style.paddingBottom = 0;
            stage.style.marginBottom = 0;
            Text(titleOverlay, Chapter.characterName + " | " + Chapter.title + " | " + difficulty, 22, true);
            if (patientPreview == null) patientPreview = gameObject.AddComponent<PatientPreview>();
            if (!patientPreview.Show(patientView))
                Text(stage, "Patient view unavailable. Open PatientScene with its Patient object and Main Camera.");
            
            {
                voiceStatus = new Label("Connect the " + Chapter.ParticipantLabel.ToLowerInvariant() + ". They will wait for you to introduce yourself.");
                voiceStatus.style.whiteSpace = WhiteSpace.Normal;
                voiceStatus.style.fontSize = 16;
                var statusArea = new ScrollView();
                ConfigureMenuScroll(statusArea);
                statusArea.style.height = 40; statusArea.style.flexShrink = 0;
                statusArea.Add(voiceStatus); stage.Add(statusArea);
                if (captions)
                {
                    var captionArea = new ScrollView();
                    ConfigureMenuScroll(captionArea);
                    captionArea.style.height = 40;
                    captionArea.style.flexShrink = 0;
                    patientCaption = new Label(Chapter.ParticipantLabel + " captions will appear here.");
                    patientCaption.style.whiteSpace = WhiteSpace.Normal;
                    patientCaption.style.fontSize = 16;
                    captionArea.Add(patientCaption); stage.Add(captionArea);
                }
                connectButton = new Button(() => conversation.Begin(Chapter, difficulty)) { text = "Connect " + Chapter.ParticipantLabel };
                StyleMenuButton(connectButton);
                connectButton.AddToClassList("menu-action-compact");
                var controls = new VisualElement(); controls.style.flexDirection = FlexDirection.Row;
                controls.style.flexShrink = 0;
                stage.Add(controls);
                connectButton.style.height = 40; connectButton.style.flexGrow = 1;
                controls.Add(connectButton);
                speakButton = new Button { text = "Hold to Speak (or hold Ctrl)" };
                StyleMenuButton(speakButton);
                speakButton.AddToClassList("menu-action-compact");
                speakButton.style.height = 40;
                speakButton.style.flexGrow = 2;
                speakButton.SetEnabled(false);
                speakButton.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    speakButton.CapturePointer(evt.pointerId);
                    conversation.PressToTalk();
                    evt.StopPropagation();
                });
                speakButton.RegisterCallback<PointerUpEvent>(evt =>
                {
                    conversation.ReleaseToTalk();
                    speakButton.ReleasePointer(evt.pointerId);
                });
                speakButton.RegisterCallback<PointerCaptureOutEvent>(_ => conversation.ReleaseToTalk());
                controls.Add(speakButton);
            }

            var checklist = Card(row); checklist.style.width = Length.Percent(25);
            checklist.style.position = Position.Absolute;
            checklist.style.right = 24; checklist.style.top = 24; checklist.style.bottom = 180;
            checklist.style.maxWidth = 320; checklist.style.minWidth = 240;
            checklist.style.backgroundColor = new Color(0.65f, 0.69f, 0.72f, 0.45f);
            checklist.style.paddingLeft = 16; checklist.style.paddingRight = 16;
            checklist.style.paddingTop = 16; checklist.style.paddingBottom = 16;
            checklist.style.flexShrink = 0; checklist.style.marginLeft = 20;
            Text(checklist, "Communication objectives", 23, true);
            objectiveStatus = new Label("Completion tracking uses learner transcript evidence. Quality scoring is separate.");
            objectiveStatus.style.whiteSpace = WhiteSpace.Normal;
            objectiveStatus.style.fontSize = 14;
            checklist.Add(objectiveStatus);
            var objectiveList = new ScrollView();
            ConfigureMenuScroll(objectiveList);
            objectiveList.style.flexGrow = 1; objectiveList.style.minHeight = 0;
            checklist.Add(objectiveList);
            foreach (TrainingObjective objective in Chapter.objectives)
            {
                // Static indicators avoid the theme's faded disabled-toggle text.
                var item = new VisualElement();
                item.style.flexDirection = FlexDirection.Row;
                item.style.marginBottom = 12;
                var indicator = new Label();
                indicator.style.color = Color.white;
                indicator.style.fontSize = 13;
                indicator.style.unityTextAlign = TextAnchor.MiddleCenter;
                indicator.style.width = 18; indicator.style.height = 18;
                indicator.style.flexShrink = 0; indicator.style.marginRight = 10;
                indicator.style.marginTop = 3;
                indicator.style.borderLeftWidth = 2; indicator.style.borderRightWidth = 2;
                indicator.style.borderTopWidth = 2; indicator.style.borderBottomWidth = 2;
                indicator.style.borderLeftColor = teal; indicator.style.borderRightColor = teal;
                indicator.style.borderTopColor = teal; indicator.style.borderBottomColor = teal;
                item.Add(indicator);
                var label = new Label((Chapter.CanAssess(objective) ? "" : "Pending faculty policy: ") + objective.text);
                label.style.color = ink; label.style.fontSize = 18;
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.flexShrink = 1; label.style.flexGrow = 1;
                label.style.flexBasis = 0;
                item.Add(label);
                objectiveIndicators.Add(objective.id, indicator);
                objectiveLabels.Add(objective.id, label);
                objectiveList.Add(item);
            }
            Text(checklist, "Coach feedback", 20, true);
            
            {
                var coachingArea = new ScrollView();
                ConfigureMenuScroll(coachingArea);
                coachingArea.style.height = 96; coachingArea.style.flexShrink = 0;
                coachingStatus = new Label(); coachingStatus.style.fontSize = 13;
                coachingStatus.style.whiteSpace = WhiteSpace.Normal;
                coachingText = new Label("Brief feedback will appear here between turns.");
                coachingText.style.color = ink; coachingText.style.fontSize = 16;
                coachingText.style.whiteSpace = WhiteSpace.Normal;
                coachingArea.Add(coachingStatus); coachingArea.Add(coachingText);
                checklist.Add(coachingArea);
            }

            
            {
                var retry = new Button(() => conversation.Objectives.Retry()) { text = "Retry Objective Check" };
                StyleMenuButton(retry);
                retry.AddToClassList("menu-action-compact");
                retry.style.height = 30; checklist.Add(retry);
            }
            var navigation = new VisualElement(); navigation.style.flexDirection = FlexDirection.Row;
            navigation.style.position = Position.Absolute;
            navigation.style.left = 24; navigation.style.right = 24; navigation.style.bottom = 16;
            navigation.style.flexShrink = 0; content.Add(navigation);
            pauseButton = Action(navigation, "Pause", () => conversation.TogglePause());
            Action(navigation, "End Session | Review", () => RequestNavigation(Summary, "End and Review", false));
            Action(navigation, "Chapter Selection", () => RequestNavigation(Chapters, "Exit to Chapters", true));
            Action(navigation, "Exit to Menu", () => RequestNavigation(Menu, "Exit to Menu", true));
            foreach (var button in navigation.Children()) button.style.flexGrow = 1;
        }

        private void RequestNavigation(System.Action destination, string confirmLabel, bool alwaysConfirm)
        {
            if (!conversation.IsBusy || (!alwaysConfirm && !conversation.VoiceBusy)) { destination(); return; }
            if (navigationPrompt != null) return;
            pendingNavigation = destination;
            conversation.SetNavigationPrompt(true);
            content.SetEnabled(false);
            navigationPrompt = new VisualElement();
            navigationPrompt.style.position = Position.Absolute;
            navigationPrompt.style.left = navigationPrompt.style.right = navigationPrompt.style.top = navigationPrompt.style.bottom = 0;
            navigationPrompt.style.backgroundColor = new Color(0.03f, 0.10f, 0.13f, 0.78f);
            navigationPrompt.style.alignItems = Align.Center;
            navigationPrompt.style.justifyContent = Justify.Center;
            root.Add(navigationPrompt);
            var dialog = Card(navigationPrompt); dialog.style.backgroundColor = new Color(0.88f, 0.91f, 0.93f, 0.94f); dialog.style.width = Length.Percent(80); dialog.style.maxWidth = 560;
            dialog.style.flexShrink = 0;
            Text(dialog, "End this conversation?", 27, true);
            Text(dialog, "Confirming stops the microphone, character audio, and voice connection. Unsent recording is discarded; available transcript and results are saved locally.");
            Text(dialog, "Current speech may continue until you confirm. Cancel returns to the conversation without ending the attempt.", 16);
            Action(dialog, confirmLabel, () =>
            {
                var action = pendingNavigation;
                CloseNavigationPrompt();
                action?.Invoke();
            });
            var cancel = Action(dialog, "Cancel", CloseNavigationPrompt);
            cancel.Focus();
        }

        private void CloseNavigationPrompt()
        {
            if (navigationPrompt == null) return;
            navigationPrompt.RemoveFromHierarchy(); navigationPrompt = null; pendingNavigation = null;
            content?.SetEnabled(true);
            if (conversation != null) conversation.SetNavigationPrompt(false);
            if (pauseButton != null && conversation.IsPaused) pauseButton.Focus();
            else if (speakButton != null && conversation.CanSpeak) speakButton.Focus();
        }
        private void Summary()
        {
            Page("Your conversation review", Chapter.title);
            saveStatus = new Label(conversation.History.Status);
            saveStatus.style.whiteSpace = WhiteSpace.Normal; saveStatus.style.fontSize = 16;
            content.Add(saveStatus);
            conversation.Assessment.Request();
            assessmentView = new VisualElement(); content.Add(assessmentView);
            RefreshAssessment();
            var goals = Card(content);
            Text(goals, "Chapter objectives and evidence", 23, true);
            objectiveStatus = new Label(); objectiveStatus.style.whiteSpace = WhiteSpace.Normal;
            goals.Add(objectiveStatus);
            objectiveReview = new VisualElement(); goals.Add(objectiveReview);
            RefreshObjectiveReview();
            
                Action(goals, "Retry Objective Check", () => conversation.Objectives.Retry());
            DrawCoachingReview();
            DrawConversationReview();
            Action(content, "Retry Chapter", Session);
            Action(content, "Chapter Selection", Chapters);
            Action(content, "Main Menu", Menu);
        }

        private void RefreshAssessment()
        {
            if (assessmentView == null) return;
            assessmentView.Clear();
            var assessment = conversation.Assessment;
            var result = assessment.Result;
            var score = Card(assessmentView);
            Text(score, "Provisional practice assessment", 26, true);
            Text(score, "Draft rubric - awaiting healthcare faculty review. Scores are for practice and are not validated clinical assessments.", 16);
            Text(score, result?.OverallScore != null ? $"Overall score: {result.OverallScore}/100" : "Overall score: Not available", 28, true);
            Text(score, assessment.Status);
            Text(score, "The quality score uses five equally weighted categories. Objective completion is reported separately and does not change this score.", 16);
            if (!string.IsNullOrWhiteSpace(Chapter.PolicyNotice)) Text(score, Chapter.PolicyNotice, 16);
            if (result != null)
            {
                foreach (var category in assessment.Rubric.Categories)
                {
                    var rating = result.Categories.Find(c => c.Id == category.Id);
                    var card = Card(assessmentView);
                    Text(card, category.Label + " | " + (rating.Rating.HasValue ? rating.Rating + "/4" : "Insufficient opportunity"), 22, true);
                    Text(card, rating.Rationale, 18);
                    DrawAssessmentEvidence(card, rating.Evidence);
                }
                DrawAssessmentFindings("What went well", result.Strengths, false);
                DrawAssessmentFindings("Areas for improvement and suggested wording", result.Improvements, true);
            }
            var rubric = assessment.Rubric;
            if (rubric != null)
            {
                var details = new Foldout { text = "View draft scoring rubric (" + rubric.Version + ")", value = false };
                score.Add(details);
                foreach (var category in rubric.Categories) Text(details, category.Label + ": " + category.Weight + "%", 16);
                foreach (var anchor in (Newtonsoft.Json.Linq.JObject)rubric.Configuration["scale"])
                    Text(details, anchor.Key + "/4: " + (string)anchor.Value, 16);
                Text(details, "Overall = sum of (category rating / 4 × category weight), rounded to the nearest whole number. All five ratings and a complete transcript are required.", 16);
            }
            var retry = new Button(() => assessment.Request(true)) { text = "Retry Final Assessment" };
            StyleMenuButton(retry);
            retry.AddToClassList("menu-action-compact");
            retry.style.height = 40; retry.SetEnabled(!assessment.IsEvaluating);
            score.Add(retry);
        }

        private void DrawAssessmentFindings(string title, System.Collections.Generic.List<AssessmentFinding> findings, bool improvement)
        {
            var card = Card(assessmentView); Text(card, title, 23, true);
            if (findings.Count == 0) Text(card, "No sufficiently supported observations were returned.", 16);
            foreach (var finding in findings)
            {
                Text(card, finding.Message, 18, true);
                DrawAssessmentEvidence(card, finding.Evidence);
                if (improvement) Text(card, "Suggested wording: " + finding.Alternative, 18);
            }
        }

        private void DrawAssessmentEvidence(VisualElement container, System.Collections.Generic.List<AssessmentQuote> evidence)
        {
            foreach (var quote in evidence)
            {
                string time = "";
                if (conversation.Transcript != null)
                    foreach (var turn in conversation.Transcript.Turns)
                        if (turn.ItemId == quote.ItemId && turn.Speaker == quote.Speaker)
                            time = $"{(int)turn.ElapsedSeconds / 60:00}:{(int)turn.ElapsedSeconds % 60:00} ";
                Text(container, time + DisplaySpeaker(quote.Speaker) + ": \"" + quote.Quote + "\"", 16);
            }
        }

        private void DrawCoachingReview()
        {
            var card = Card(content);
            Text(card, "During-session coaching", 23, true);
            var history = conversation.Coach.Feed.History;
            if (history.Count == 0)
            {
                Text(card, "No coaching messages were shown during this attempt.");
                return;
            }
            foreach (var message in history)
            {
                Text(card, (message.Kind == "positive" ? "What worked: " : "Try next time: ") + message.Message, 18, true);
                if (!string.IsNullOrWhiteSpace(message.Alternative)) Text(card, "Suggested wording: " + message.Alternative, 16);
                foreach (var evidence in message.Evidence)
                    Text(card, DisplaySpeaker(evidence.Speaker) + ": \"" + evidence.Quote + "\"", 16);
            }
            Text(card, "AI communication suggestions are separate from objective completion and the final performance score.", 14);
        }

        private void DrawConversationReview()
        {
            var review = Card(content);
            Text(review, "Conversation review", 23, true);
            var transcript = conversation.Transcript;
            if (transcript == null || transcript.ChapterId != Chapter.id || transcript.Turns.Count == 0)
            {
                Text(review, "No conversation captured for this attempt. Connect the character and speak to begin.");
                return;
            }
            Text(review, transcript.Difficulty + " | " + transcript.StartedUtc.ToLocalTime().ToString("g"), 16);
            Text(review, "Times show when turns entered this session, not exact audio timing. Automatic transcription may contain errors. Patient text reflects generated speech and may include unplayed words if you ended early.", 16);
            Text(review, "Ended attempts are saved locally on this device. Open View Progress to review or delete them.", 16);
            foreach (var turn in transcript.Turns)
            {
                int minutes = (int)turn.ElapsedSeconds / 60;
                int seconds = (int)turn.ElapsedSeconds % 60;
                string state = turn.State == TranscriptState.Complete ? "" : " | " + turn.State;
                Text(review, $"{minutes:00}:{seconds:00}  {DisplaySpeaker(turn.Speaker)}{state}", 18, true);
                var text = new TextField { multiline = true, isReadOnly = true,
                    value = string.IsNullOrWhiteSpace(turn.Text) ? "Transcript unavailable for this turn." : turn.Text };
                text.style.whiteSpace = WhiteSpace.Normal;
                text.style.marginBottom = 18;
                text.style.fontSize = 18;
                review.Add(text);
            }
        }

        private void RefreshObjectiveReview()
        {
            if (objectiveReview == null) return;
            objectiveReview.Clear();
            var tracker = conversation.Objectives;
            var transcript = conversation.Transcript;
            bool fullyReviewed = transcript != null && transcript.Turns.Count > 0;
            bool hasLearner = false;
            if (transcript != null)
                foreach (var turn in transcript.Turns)
                    if (turn.Speaker == "You")
                    {
                        hasLearner = true;
                        if (turn.State != TranscriptState.Complete || !tracker.Evidence.WasReviewed(turn.ItemId)) fullyReviewed = false;
                    }
            Text(objectiveReview, $"Steps observed: {tracker.Evidence.CompletedCount}/{Chapter.objectives.Count(Chapter.CanAssess)} available", 20, true);
            foreach (var objective in Chapter.objectives)
            {
                if (!Chapter.CanAssess(objective))
                {
                    Text(objectiveReview, "Pending faculty policy: " + objective.text, 16);
                    continue;
                }
                if (tracker.Evidence.TryGet(objective.id, out var match))
                {
                    Text(objectiveReview, "Completed: " + objective.text, 18, true);
                    foreach (var evidence in match.Evidence)
                    {
                        var turn = transcript?.Turns;
                        string time = "";
                        if (turn != null)
                            foreach (var entry in turn)
                                if (entry.ItemId == evidence.ItemId && entry.Speaker == "You")
                                    time = $"{(int)entry.ElapsedSeconds / 60:00}:{(int)entry.ElapsedSeconds % 60:00}";
                        Text(objectiveReview, time + " You: \"" + evidence.Quote + "\"", 16);
                    }
                }
                else Text(objectiveReview, (fullyReviewed && hasLearner ? "Not observed: " : "Not fully assessed: ") + objective.text, 16);
            }
            Text(objectiveReview, "AI identifies whether a step occurred; quoted evidence is checked against your transcript. This does not grade quality or establish clinical correctness.", 14);
        }
        private void Progress()
        {
            Page("Your progress", "Saved practice attempts on this device. Scores use a provisional rubric.");
            var attempts = conversation.History.Load();
            Text(content, conversation.History.Status, 16);
            if (conversation.History.UnreadableCount > 0)
                Text(content, "Some saved attempts could not be read. Their files have not been changed.", 16);
            Text(content, attempts.Count + " saved attempts | " + System.Linq.Enumerable.Distinct(System.Linq.Enumerable.Select(attempts, a => a.ChapterId)).Count() + " chapters practiced", 22, true);
            Text(content, "Transcript and feedback are stored as local text. Only use fictional training conversations. Use Delete on an attempt to remove its record.", 16);
            Text(content, "Comparisons match the same chapter, difficulty, and rubric. Practiced chapters do not indicate clinical proficiency.", 16);
            Action(content, "Refresh Saved Attempts", Progress);
            if (attempts.Count == 0) Text(content, "No saved attempts yet. Complete a conversation to create one.");
            foreach (var attempt in attempts)
            {
                var item = Card(content);
                Text(item, attempt.ChapterTitle, 23, true);
                Text(item, attempt.StartedUtc.ToLocalTime().ToString("g") + " | " + attempt.Difficulty, 16);
                Text(item, "Provisional score: " + (attempt.Assessment?.OverallScore.HasValue == true ? attempt.Assessment.OverallScore + "/100" : "Not available") +
                    " | Objectives observed: " + attempt.Objectives.FindAll(o => o.Completed).Count + "/" + attempt.Objectives.Count(o => !o.PendingFacultyPolicy) + " available", 18);
                Action(item, "Open Saved Results", () => SavedResults(attempt));
                var earlier = attempts.Find(a => a.Id != attempt.Id && a.StartedUtc < attempt.StartedUtc && AttemptStore.CanCompare(a, attempt));
                if (earlier != null) Action(item, "Compare with Previous Matching Attempt", () => CompareAttempts(earlier, attempt));
                var delete = new Button { text = "Delete Attempt" }; StyleMenuButton(delete); delete.AddToClassList("menu-action-compact"); delete.style.height = 36;
                var confirmation = new VisualElement(); confirmation.style.display = DisplayStyle.None;
                Text(confirmation, "Delete this attempt's transcript and results from this device?", 16);
                Action(confirmation, "Yes, Delete", () => { conversation.History.Delete(attempt.Id); Progress(); });
                Action(confirmation, "Cancel", () => confirmation.style.display = DisplayStyle.None);
                delete.clicked += () => confirmation.style.display = DisplayStyle.Flex;
                item.Add(delete); item.Add(confirmation);
            }
            Action(content, "Explore Chapters", Chapters); Action(content, "Main Menu", Menu);
        }
        private void SavedResults(SavedAttempt attempt)
        {
            Page("Saved conversation review", attempt.ChapterTitle + " | " + attempt.Difficulty + " | " + attempt.StartedUtc.ToLocalTime().ToString("g"));
            Text(content, "Saved snapshot. Opening this record does not rerun any assessment.", 16);
            var score = Card(content);
            Text(score, "Provisional score: " + (attempt.Assessment?.OverallScore.HasValue == true ? attempt.Assessment.OverallScore + "/100" : "Not available"), 28, true);
            Text(score, "Draft rubric - not a validated clinical assessment. Objective completion is separate.", 16);
            if (!string.IsNullOrWhiteSpace(attempt.PolicyNotice)) Text(score, attempt.PolicyNotice, 16);
            Text(score, attempt.AssessmentWasPending ? "Assessment had not finished when this snapshot was saved." : attempt.AssessmentStatus, 16);
            if (attempt.Rubric != null) Text(score, "Rubric: " + (string)attempt.Rubric["version"], 16);
            if (attempt.Assessment != null)
            {
                foreach (var category in attempt.Assessment.Categories)
                {
                    var card = Card(content);
                    Text(card, SavedCategoryLabel(attempt, category.Id) + " | " + (category.Rating.HasValue ? category.Rating + "/4" : "Insufficient opportunity"), 22, true);
                    Text(card, category.Rationale);
                    SavedEvidence(card, category.Evidence, attempt);
                }
                foreach (bool improvement in new[] { false, true })
                {
                    var card = Card(content);
                    Text(card, improvement ? "Areas for improvement" : "What went well", 23, true);
                    var findings = improvement ? attempt.Assessment.Improvements : attempt.Assessment.Strengths;
                    if (findings.Count == 0) Text(card, "No supported observations were returned.", 16);
                    foreach (var finding in findings)
                    {
                        Text(card, finding.Message, 18, true);
                        SavedEvidence(card, finding.Evidence, attempt);
                        if (improvement) Text(card, "Suggested wording: " + finding.Alternative, 18);
                    }
                }
            }
            var objectives = Card(content);
            Text(objectives, "Communication objectives", 23, true);
            Text(objectives, attempt.ObjectiveStatus, 16);
            foreach (var objective in attempt.Objectives)
            {
                Text(objectives, (objective.PendingFacultyPolicy ? "Pending faculty policy: " : objective.Completed ? "Completed: " : attempt.AllLearnerTurnsReviewed ? "Not observed: " : "Not fully assessed: ") + objective.Text, 18, objective.Completed);
                foreach (var quote in objective.Evidence)
                    Text(objectives, SavedTime(attempt, quote.ItemId, "You") + "You: \"" + quote.Quote + "\"", 16);
            }
            var coaching = Card(content); Text(coaching, "During-session coaching", 23, true);
            if (attempt.Coaching.Count == 0) Text(coaching, "No coaching messages were shown.", 16);
            foreach (var message in attempt.Coaching)
            {
                Text(coaching, (message.Kind == "positive" ? "What worked: " : "Consider: ") + message.Message, 18, true);
                foreach (var quote in message.Evidence) Text(coaching, SavedSpeaker(attempt, quote.Speaker) + ": \"" + quote.Quote + "\"", 16);
                if (!string.IsNullOrWhiteSpace(message.Alternative)) Text(coaching, "Suggested wording: " + message.Alternative, 16);
            }
            var dialogue = Card(content);
            Text(dialogue, "Conversation transcript", 23, true);
            Text(dialogue, "Automatic transcription may contain errors. Times mark turn events. Patient text can include generated speech that was not played before exit.", 16);
            foreach (var turn in attempt.Turns)
            {
                Text(dialogue, SavedTime(attempt, turn.ItemId, turn.Speaker) + SavedSpeaker(attempt, turn.Speaker) +
                    (turn.State == TranscriptState.Complete ? "" : " | " + turn.State), 18, true);
                var text = new TextField { multiline = true, isReadOnly = true,
                    value = string.IsNullOrWhiteSpace(turn.Text) ? "Transcript unavailable." : turn.Text };
                text.style.marginBottom = 18; text.style.whiteSpace = WhiteSpace.Normal; dialogue.Add(text);
            }
            int index = System.Array.FindIndex(catalog.chapters, c => c.id == attempt.ChapterId);
            if (index >= 0) Action(content, "Practice This Chapter Again", () => { selected = index; difficulty = attempt.Difficulty; Briefing(); });
            Action(content, "View Progress", Progress); Action(content, "Main Menu", Menu);
        }

        private string SavedCategoryLabel(SavedAttempt attempt, string id)
        {
            if (attempt.Rubric?["categories"] is Newtonsoft.Json.Linq.JArray categories)
                foreach (var category in categories) if ((string)category["id"] == id) return (string)category["label"];
            return id;
        }
        private string SavedSpeaker(SavedAttempt attempt, string speaker) => speaker == "Patient" ?
            (string.IsNullOrWhiteSpace(attempt.ParticipantLabel) ? "Patient" : attempt.ParticipantLabel) : speaker;
        private string SavedTime(SavedAttempt attempt, string id, string speaker)
        {
            var turn = attempt.Turns.Find(t => t.ItemId == id && t.Speaker == speaker);
            return turn == null ? "" : $"{(int)turn.ElapsedSeconds / 60:00}:{(int)turn.ElapsedSeconds % 60:00} ";
        }
        private void SavedEvidence(VisualElement parent, System.Collections.Generic.List<AssessmentQuote> evidence, SavedAttempt attempt)
        {
            foreach (var quote in evidence) Text(parent, SavedTime(attempt, quote.ItemId, quote.Speaker) + SavedSpeaker(attempt, quote.Speaker) + ": \"" + quote.Quote + "\"", 16);
        }

        private void CompareAttempts(SavedAttempt earlier, SavedAttempt latest)
        {
            Page("Compare your practice", latest.ChapterTitle + " | " + latest.Difficulty);
            if (!AttemptStore.CanCompare(earlier, latest))
            {
                Text(content, "These attempts use different chapters, difficulties, or rubrics and cannot be directly compared.");
                Action(content, "View Progress", Progress); return;
            }
            Text(content, "Same chapter, difficulty, and rubric. These are provisional AI practice results, not proof of a change in clinical skill.", 16);
            Text(content, "Earlier: " + earlier.StartedUtc.ToLocalTime().ToString("g") + " | Latest: " + latest.StartedUtc.ToLocalTime().ToString("g"), 18, true);
            var card = Card(content);
            int? before = earlier.Assessment?.OverallScore, after = latest.Assessment?.OverallScore;
            Text(card, "Overall score: " + (before.HasValue ? before + "/100" : "Not available") + " → " + (after.HasValue ? after + "/100" : "Not available"), 26, true);
            if (before.HasValue && after.HasValue) Text(card, "Change: " + (after.Value - before.Value).ToString("+0;-0;0") + " points");
            if (latest.Assessment != null && earlier.Assessment != null)
                foreach (var rating in latest.Assessment.Categories)
                {
                    var old = earlier.Assessment.Categories.Find(c => c.Id == rating.Id);
                    Text(card, SavedCategoryLabel(latest, rating.Id) + ": " +
                        (old?.Rating.HasValue == true ? old.Rating + "/4" : "Not assessed") + " → " +
                        (rating.Rating.HasValue ? rating.Rating + "/4" : "Not assessed"), 18);
                }
            Text(card, "Objectives observed: " + earlier.Objectives.Count(o => o.Completed) + "/" + earlier.Objectives.Count(o => !o.PendingFacultyPolicy) +
                " → " + latest.Objectives.Count(o => o.Completed) + "/" + latest.Objectives.Count(o => !o.PendingFacultyPolicy) + " available", 20, true);
            if (!earlier.Objectives.Select(o => o.Id).OrderBy(id => id).SequenceEqual(latest.Objectives.Select(o => o.Id).OrderBy(id => id)))
                Text(card, "The objective sets differ; their coverage counts are not directly comparable.", 16);
            Action(content, "Open Earlier Results", () => SavedResults(earlier));
            Action(content, "Open Latest Results", () => SavedResults(latest));
            Action(content, "View Progress", Progress);
        }

        private void Settings()
        {
            Page("Settings", "Adjust your screen preview preferences.");
            var card = Card(content);
            var toggle = new Toggle("Show captions") { value = captions };
            toggle.RegisterValueChangedCallback(e => captions = e.newValue); card.Add(toggle);
            Text(card, "Preview preferences apply during this Play session.", 16);
            Action(content, "Main Menu", Menu);
        }
        private void OnDestroy()
        {
            if (panel != null) Destroy(panel);
        }

        private void Update()
        {
            if (sessionVisible && conversation != null)
            {
                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                {
                    if (navigationPrompt != null) CloseNavigationPrompt();
                    else conversation.TogglePause();
                }
                if (pauseButton != null)
                {
                    pauseButton.text = conversation.IsPaused ? "Resume" : "Pause";
                    pauseButton.SetEnabled(conversation.IsPaused || conversation.CanPause);
                    pauseButton.tooltip = "Pause between turns. Escape also pauses/resumes; it cancels an open exit prompt.";
                }
            }
            if (saveStatus != null && conversation != null) saveStatus.text = conversation.History.Status;
            if (assessmentView != null && conversation != null && assessmentRevision != conversation.Assessment.Revision)
            {
                assessmentRevision = conversation.Assessment.Revision;
                RefreshAssessment();
            }
            if (coachingText != null && conversation != null)
            {
                var coach = conversation.Coach;
                coachingStatus.text = coach.Status;
                var message = coach.Feed.Current;
                if (message == null) coachingText.text = "Brief feedback will appear here between turns.";
                else
                {
                    coachingText.text = (message.Kind == "positive" ? "Good approach: " : "Consider: ") + message.Message +
                        (string.IsNullOrWhiteSpace(message.Alternative) ? "" : "\nTry: " + message.Alternative);
                    coachingText.tooltip = string.Join("\n", message.Evidence.ConvertAll(e => DisplaySpeaker(e.Speaker) + ": " + e.Quote));
                }
            }
            if (conversation != null && objectiveStatus != null)
            {
                var tracker = conversation.Objectives;
                objectiveStatus.text = tracker.Status;
                if (objectiveRevision != tracker.Revision)
                {
                    objectiveRevision = tracker.Revision;
                    foreach (var pair in objectiveIndicators)
                    {
                        bool done = tracker.Evidence.TryGet(pair.Key, out var match);
                        bool available = Chapter.CanAssess(Chapter.objectives.First(o => o.id == pair.Key));
                        pair.Value.text = done ? "✓" : "";
                        pair.Value.style.backgroundColor = done ? teal : Color.clear;
                        objectiveLabels[pair.Key].tooltip = !available ? "Not assessed until a reviewed confidentiality policy and reference are configured." : done
                            ? string.Join("\n", match.Evidence.ConvertAll(e => "You: " + e.Quote))
                            : "No verified completion evidence yet.";
                    }
                    RefreshObjectiveReview();
                }
            }
            if (voiceStatus == null || conversation == null) return;
            if (conversation.IsBusy || conversation.Status != "Select Connect Patient when you are ready.")
                voiceStatus.text = conversation.Status;
            connectButton.SetEnabled(!conversation.IsBusy);
            speakButton.SetEnabled(conversation.CanSpeak);
            if (patientCaption != null && !string.IsNullOrWhiteSpace(conversation.Caption))
                patientCaption.text = conversation.Caption;
        }
    }
}
