# PATRICIA screen prototype

## Preview in Unity

1. Open this project using Unity Hub. The repository specifies **6000.4.0f1**. Install that version to avoid upgrading the shared project.
2. Open `Assets/Scenes/PatientScene.unity`.
3. Choose **Capstone > Add Starter Training Flow** from Unity's top menu.
4. Press **Play**. The runtime UI Toolkit interface appears over the existing scene.
5. Click through Main Menu, Chapter Selection, Training, Summary, Progress, and Settings.
6. Exit Play mode. Save a separate copy of the scene as `Assets/Scenes/TrainingScreens.unity` if you want to retain the component without changing the team's original scene.

All six chapters have distinct fictional draft profiles and nine objectives each, configured in `Assets/Resources/TrainingScenarios.json`. Start Training opens the first chapter's briefing; chapter cards open their selected briefing. Briefings show background, setting, difficulty, and learning objectives. All chapters now share voice interaction, a two-sided conversation review, automatic objective tracking, between-turn coaching, provisional final assessment, and local history. Their role, background, objectives, and difficulty instructions differ by scenario. Difficulty and captions are session-only preferences. Retry returns to the selected chapter and requires a fresh voice connection.

Profiles include opening lines, concerns, private disclosures, and planned reactions to empathy or judgment. Private disclosures are omitted from the learner UI. `TrainingScenario.BuildPatientInstructions` supplies the alcohol chapter's profile and difficulty instructions to the realtime client when Connect Patient is pressed. The learner opens the conversation; an opening line is not automatically generated. Difficulty influences the model through instructions and is not a deterministic behavioral guarantee. Profiles are fictional drafts for healthcare faculty review; confidentiality policy for the adolescent scenario must be supplied for the intended setting and jurisdiction.

The training screen now displays the existing `Patient` avatar through a dedicated runtime camera and render texture. The scene's Main Camera is preserved. The objective checklist stays in the right column and scrolls independently. All chapters currently share the existing avatar and scene-configured voice; scenario-specific appearance, casting, and emotional animation are not yet connected. Briefings disclose this shared visual placeholder. The preview camera runs only on the training screen, and its render texture is released when the component is destroyed.

The layout lives in `Assets/Scripts/TrainingFlow.cs`. The editor setup command lives in `Assets/Editor/TrainingSetup.cs`. Unity UI Toolkit is included in Unity; no new package is required. The UI is built at runtime, so its controls appear in Play mode rather than as editable Canvas children.

## Temporary main-menu guide

The main menu uses the repository's nurse model as a replaceable practice guide. It has a separate live portrait, soft lighting, a looping idle animation, and a greeting wave. A text welcome and Getting started, About feedback, and My results help buttons explain navigation. Hovering or keyboard-focusing the four primary menu buttons updates the guide text. This is a text welcome, not synthesized speech; no microphone, patient connection, or API call is started by the guide.

`MenuGuideSetup` prepares `Assets/Resources/MenuGuide.prefab` and a dedicated Idle/Wave controller after Unity reloads scripts in Edit mode. It reuses the existing nurse FBX and imported animation clips without changing the originals or the team's scenes. It uses a temporary preview scene when constructing the prefab and preserves an existing guide prefab/controller rather than overwriting later customizations. If the portrait is missing, stop Play and choose Capstone > Create Menu Guide Assets, then press Play again. Wait for asset importing to finish first.

`MenuGuidePresenter` owns a separate stage, camera, lights, and render texture. Its cloned guide uses layer 31 and is placed away from the training environment; leaving the menu disables the stage. No patient animator, scene camera, voice setting, or scenario profile is changed. The portrait texture is released when the component is destroyed. The existing avatar is a temporary visual choice, not a final character design.

To replace it later, stop Play and assign the team's imported character prefab to the Menu Guide Prefab field on Starter Training Flow. Keep a humanoid avatar and looping idle controller on that prefab; a trigger named Wave enables the greeting gesture. A prefab without that trigger still renders without trying to play a nonexistent gesture. The prefab should contain the intended model and animator rather than gameplay or patient-conversation scripts. Character framing and animation retargeting need a visual check after replacement.

## Try chapter voice practice

1. Configure a replacement OpenAI API key as the Windows user environment variable `OPENAI_API_KEY`. Search Windows for **Edit environment variables for your account**, choose **New** under user variables, and enter that name and the key locally. Do not paste it into chat, code, or a scene.
2. Fully close and reopen Unity Hub and Unity so the editor inherits the variable. The API project must have Realtime access and available billing/quota; ChatGPT access alone does not configure an API project.
3. Open PatientScene (or the saved TrainingScreens copy), press Play, then Start Training, review the briefing, and select Enter Training.
4. Select **Connect Patient**. Wait for the ready message. No connection is opened just by browsing a chapter.
5. With the Game view focused, hold **Ctrl**, introduce yourself, and release to send. Alternatively, hold the on-screen **Speak** button. The patient waits for you to begin.
6. Wait for the spoken reply. Patient captions show the current response when enabled. Repeat for another turn. Each recording is limited to approximately 30 seconds.
7. End Session stops recording, clears queued playback, disconnects, and opens the summary. Scroll to **Conversation review** to inspect both learner and patient turns. Retry starts a fresh conversation without the previous attempt's context. Chapter Selection also disconnects.

Connection, API, and microphone failures appear in the training screen's status area. If no microphone is detected, check the selected Windows input device and permission for desktop apps to access the microphone. Use fictional practice speech: audio is sent to OpenAI when released. Captions show the current patient response; the summary provides the full current-attempt review. Neither is an assessment.

## Conversation review

`ConversationTranscript.cs` stores identified turns in memory. Learner turns are reserved when audio is committed; patient turns are reserved when their output item is added. Later transcription completions fill those entries rather than changing turn order. Final patient transcripts replace streamed fragments. The Realtime session enables input transcription using the configurable `transcriptionModel` on OpenAIRealtimeClient (default `gpt-transcribe`); this also requires appropriate API model access and incurs transcription usage.

Elapsed timestamps mark local receipt of turn events, not word-level or microphone-start timing. Input transcription is an automatic approximation of speech. Unfinished entries are labeled Partial or Unavailable when the session ends; no missing words are invented. Patient transcript text records generated output, which may include unplayed speech if the learner exits early. Summary text is read-only and selectable.

Entering another training preview clears the live review, and reconnecting creates a new attempt. Ended attempts with transcript turns are saved locally, including transcript text and results. No raw audio recording or API credential is saved. See the [Realtime transcription documentation](https://developers.openai.com/api/docs/guides/realtime-transcription) for the event protocol.

## Existing voice prototype

Without the TrainingFlow component, the original realtime client still connects on Start. Its source now reads `OPENAI_API_KEY` rather than an embedded credential. Any previously committed key needs to be revoked and replaced by its owner. Never paste the replacement into source or a Unity scene. Scene-specific voice/model configuration is otherwise preserved.

The direct-key connection is for local development. Before distributing a build, move credential handling to a backend. Voice integration follows the [OpenAI Realtime conversation event flow](https://developers.openai.com/api/docs/guides/realtime-conversations). The selected scene's model and voice settings remain in use.

## Automatic objective completion

Each chapter automatically checks the available transcript after completed learner or patient transcription events, with a short debounce and one request in flight at a time. `ObjectiveTracker` calls the Responses API using the same `OPENAI_API_KEY` environment variable. Its text model defaults to `gpt-4.1-mini`; this is additional API usage and requires model access. It uses [structured outputs](https://developers.openai.com/api/docs/guides/structured-outputs), `store: false`, a 30-second timeout, and no background retry loop.

The evaluator identifies whether a communication step occurred, not how effectively it was performed. Compound objectives must satisfy all parts. It must cite exact contiguous quotes from identified, Complete learner turns. The client rejects an entire result batch if any objective ID, source turn, or quote is invalid, including quotes from patient or partial speech. Requests include dialogue as data and instruct the model to ignore attempts in speech to alter evaluation rules. These safeguards validate source evidence, not the correctness of the model's semantic judgment; faculty review and live calibration are still needed.

Completed items show a teal check. Hover over the objective text to see evidence, or open the summary to view the quotes and their turn timestamps. Unmatched objectives are labeled Not observed only when all available learner turns are Complete and reviewed; otherwise they remain Not fully assessed. API failures preserve existing evidence, allow voice to continue, and offer Retry Objective Check. Missing speech is never counted as proof of failure.

An in-flight check may finish while you view the summary and update that attempt's saved record. Starting a new attempt cancels the previous request and clears its live evidence; stale responses cannot affect the next attempt. Objective coverage does not produce a communication-quality score.

## Real-time communication coaching

Each chapter's Coach feedback panel provides brief positive observations or actionable improvements. `CommunicationCoach` checks completed learner turns using the Responses API, the same environment key, and a configurable text model (default `gpt-4.1-mini`). This is additional API usage separate from voice and objective tracking. Only Complete dialogue up to the designated learner turn is supplied as context. One request runs at a time; newer learner turns replace queued older work.

`CoachingProtocol` requires an exact quote from the designated learner turn. Empathy improvements additionally require an exact patient quote. Unsupported categories, invented or partial evidence, malformed outputs, and claims about interruptions or nonverbal behavior are rejected. Allowed categories cover empathy, nonjudgmental language, reflective listening, autonomy, open questions, and clarity. The model can return no observation; praise or criticism is not mandatory. Improvements include suggested wording. Source validation does not prove that an AI interpretation is correct: content still needs faculty review and calibration.

`CoachingFeed` displays a maximum of one message per learner turn, only while microphone capture/submission, patient generation, and playback are idle. Messages are spaced by at least 15 seconds, consecutive repeated category/kind messages are suppressed, and feedback for an earlier learner turn is discarded when the conversation moves on. It never speaks, pauses the conversation, opens a modal, or changes checklist evidence or scores. A message that is already visible remains readable during later speech.

Hover over a message for its quotes, or open End Session | Review to see the shown coaching history and evidence. Ending the conversation cancels pending coaching; starting another attempt clears history. Service failures appear in the feedback area and do not stop voice practice. Transcript text cannot establish interruptions; audio timing instrumentation remains future work.

## Pause and session navigation

Training now has Pause/Resume, End Session | Review, Chapter Selection, and Exit to Menu controls. Pause becomes available only after the voice connection is ready and microphone capture/submission, response generation, and patient playback are all idle. It disables microphone input and defers coaching display while preserving the same connection, patient context, attempt ID, and transcript. Resume re-enables input on that same conversation if the connection remains ready. The world clock is not frozen; WebSocket callbacks and background objective checks continue. If the remote session disconnects while paused, reconnecting starts a new attempt rather than pretending the lost context can be resumed.

Escape pauses/resumes between turns; when an exit prompt is open, Escape cancels that prompt instead. Chapter Selection and Exit to Menu require confirmation during an active conversation. End Session opens review directly between turns, but asks for confirmation if speech is in progress. Confirmation blocks new turns and background menu interaction while permitting an existing turn to finish; it does not silently discard a recording merely because the prompt opened. Cancel preserves the previous paused or active state. The Cancel button receives initial keyboard focus.

Confirming a navigation action stops microphone recording and queued playback and closes the voice session. Existing generation callbacks are invalidated, pending audio commits are released, and the attempt is saved with whatever transcript/results are available. A recording stopped before submission is represented as an Unavailable learner turn without invented words, so incomplete-data score withholding still applies. Coaching requests stop on exit; objective and final-assessment work already associated with an ended attempt can finish and update its saved record. End Session | Review initiates final assessment; exiting directly to menu does not request a new quality score.

## Final performance assessment

Opening End Session | Review for any chapter requests one full-conversation quality assessment. The summary shows status while the request runs, the resulting category explanations and timestamped evidence, supported strengths, and improvements with exact learner quotes and suggested wording. Retry Final Assessment explicitly reruns it; there is no automatic retry loop. The same `OPENAI_API_KEY` is used for additional Responses API usage with a configurable model (default `gpt-4.1-mini`), `store: false`, and a 45-second timeout. Starting a new attempt cancels and invalidates the prior assessment.

The rubric is an authored draft in `Assets/Resources/AssessmentRubric.json`, version `draft-1`, awaiting healthcare faculty review. It is not a validated instrument or a clinical competency determination. Five categories each contribute 20%: empathy/respect, active listening, motivational interviewing, nonjudgmental communication, and clarity. Each has a 0-4 anchored rating; the local application calculates `sum(rating / 4 * weight)`, rounded to the nearest integer. Objective coverage is displayed separately and does not contribute to this quality score. Expand View draft scoring rubric in the summary to see the anchors and weights.

The draft minimum is three Complete learner turns and two Complete patient replies. A transcript with any incomplete or missing turn receives no quality score. A category without enough opportunity is rated null, not zero; any null category withholds the overall total while available category feedback remains visible. Zero is reserved for observed counterproductive communication. The model must cite learner evidence for every numeric category and finding; a top rating requires two distinct learner turns, and an empathy rating additionally requires patient context. Exact quote, speaker, identifier, category, range, and output-completion validation is enforced before showing any result. Improvements need concrete alternative wording; unsupported text-only timing/nonverbal claims are rejected. No missing data or service failure generates an invented score.

The assessment is independent of the incremental objective and coaching models; it does not consume their judgments as evidence. It uses the captured conversation. Speech recognition and AI interpretation remain fallible. Patient text can include generated but unplayed output if the learner exits early, as disclosed in the transcript review. Faculty calibration and live testing remain required. Ended attempts, rubric snapshots, and available scores are saved locally as described below.

## Scenario roles and reviewed-policy configuration

All six scenario profiles drive the same runtime rather than separate hard-coded chapter implementations. A new connection supplies only the selected character's identity, background, hidden disclosures, reactions, and difficulty instructions. The concerned-family character is explicitly a parent, not the person receiving treatment. Role-aware learner briefings and setting information are supplied to coaching, objective tracking, and final assessment; private character disclosures are not supplied as grading evidence. Internal transcript roles remain You/Patient for protocol compatibility, while family-member labels are shown in the live and saved UI.

The adolescent chapter is available as a fictional communication exercise. Its policy-specific confidentiality objective has `requiresReviewedPolicy: true`. Until both `reviewedConfidentialityPolicy` and `policyReviewReference` are populated in that scenario's JSON using healthcare faculty-reviewed content, the objective is excluded from evaluation requests, rejected if returned anyway, and labeled Pending faculty policy in live and saved reviews. It is excluded from available-objective coverage counts and is not reported as a missed step. The eight other steps remain available. No law, legal exception, or approval reference has been invented or prefilled. The application checks presence of these configuration fields, not authenticity of a human review. Policy correctness is not part of the generic communication-quality score.

Archived attempts retain the participant label, policy notice, pending-objective flags, and policy review reference. Comparisons require matching policy review references in addition to the chapter, difficulty, and rubric. Older records remain readable using the default Patient label and non-pending objectives. Scenario content and the rubric remain drafts requiring clinical review; live responses and difficulty behavior still need end-to-end calibration.

## Local attempt history and progress

`AttemptHistory` saves ended conversations containing at least one transcript turn to `Application.persistentDataPath/TrainingAttempts`, outside the source repository. `AttemptStore` uses versioned JSON records with a unique attempt ID, chapter/title, difficulty, start/end UTC times, transcript, objective evidence and review state, shown coaching, draft rubric snapshot, final assessment, and processing status. Nothing is uploaded by opening history. Local files contain unencrypted training transcript text; this feature is for fictional practice conversations. No microphone recording or API key is stored.

An initial record is written when the conversation stops. Later objective/assessment revisions replace the same record rather than creating duplicate attempts. If Play mode ends before evaluation finishes, the available snapshot remains, without an invented score. Save failures are reported in the summary; failed saves retry after a delay while the app remains running. New attempts get independent IDs and live data. The end timestamp stays fixed across repeated navigation.

View Progress lists attempts newest first, with practiced-chapter count, available provisional scores, and observed-objective coverage. Open Saved Results reopens the captured category explanations, strengths, improvement wording, objective evidence, coaching, and transcript without calling the APIs. Practice This Chapter Again opens its current briefing. Refresh Saved Attempts reloads results that may have finished since the page opened.

Compare with Previous Matching Attempt uses the same chapter, difficulty, and full rubric snapshot. It shows available score differences, category ratings, and objective counts. Missing scores remain unavailable rather than zero. Different objective sets are labeled as not directly comparable. Practice counts and provisional differences do not establish clinical proficiency.

Delete Attempt asks for confirmation and removes only that attempt's JSON file. In-session suppression prevents a late evaluator from recreating the deleted record. Files are written through a temporary sibling and replaced atomically; partial `.pending` files are not treated as saved records. Corrupt, oversized, or unsupported-version files are skipped and reported without changing them. Identifiers must be GUIDs, so record paths cannot escape the history folder. History is device-local; accounts, cloud sync, encryption, and institutional deployment policies are not implemented.

## Next screen design tasks

- Review the temporary menu guide's framing and gestures; replace it with the team's final character when available.
- Confirm typography, color palette, spacing, and laptop-sized layouts in the target Unity editor.
- Review the draft scenario profiles and objective sets with the healthcare project partner.
- Test pause/exit controls in Unity and add persistent accessibility preferences.
- Test every chapter and difficulty in Unity, including saved history, retries, role consistency, and healthcare partner review.

## Validation

The screen scripts can be compiled against installed Unity assemblies independently. This is not a substitute for opening the project in the specified Unity version and checking the Game view, interactions, and asset imports.

Run `Tests/OfflineVoice/Run.ps1` in PowerShell for offline protocol checks; pass `-UnityData` with your installed editor's Data directory if it differs from the default. The harness uses fake Unity/WebSocket surfaces, an offline placeholder credential, and no network or microphone access. It checks missing credentials, session configuration/readiness, commit acknowledgement, duplicate-response suppression, pending-commit cancellation, stale-session isolation, upload failures, delayed learner transcription, final transcript replacement, failed transcription, incomplete attempts, and fresh-attempt isolation. Real microphone, speaker, credentials, network, transcription accuracy, and patient behavior still require an end-to-end test in Unity.

The harness also checks objective/coaching evidence, pacing, rubric scoring and withholding, JSON history round trips, atomic record updates, ordering, matching comparisons, corrupt records, path validation, deletion, and prevention of deleted-record resurrection. Filesystem test fixtures use the project's ignored `Library/PatriciaOfflineTests` folder.

Session-control checks cover readiness and busy-state pause guards, speech blocking, modal cancel behavior, preservation of paused state, lost-connection resume guards, stable attempt context, reset behavior, and discarded input. Unity's actual buttons, focus, keyboard input, microphone cancellation, and playback still need live end-to-end verification.
