# Whole-App Sprint 0 Friction Findings

Scoring is `impact × frequency × comprehension burden`. Source evidence is
dated 2026-09-27; hypotheses require later rendered and user evidence. Ties are
included. Control ids link to `whole-app-sprint-0-controls.json`.

## Settings

| Id | Finding | Impact | Frequency | Burden | Score | Evidence | Owning sprint |
| --- | --- | ---: | ---: | ---: | ---: | --- | --- |
| S01 | Setup exposes profile, asset import, download, and storage actions before a normal user can record. | 5 | 5 | 3 | 75 | Source: `control-usestandardtranscriptionprofilebutton`, `control-importapprovedspeakerlabelingbutton`, and Setup section hosts. Hypothesis: too many readiness choices delay first value. | Sprint 4 |
| S02 | General and Advanced interleave daily capture preferences with provider, CLI, threshold, and timeout tuning. | 5 | 4 | 3 | 60 | Source: `control-configmiccapturecheckbox`, `control-configautodetectcheckbox`, `control-configtranscriptionclipathtextbox`, and `control-configautodetectthresholdtextbox`. Hypothesis: users cannot predict where a setting belongs. | Sprint 2 / Sprint 3 |
| S03 | Hosted-summary controls place provider, endpoint, key, model, reasoning, timeout, and chunk controls in one decision area. | 5 | 3 | 3 | 45 | Source: `control-configsummarygenerationenabledcheckbox`, `control-configsummaryopenaikeypasswordbox`, and provider validation controls. Fact: cost/privacy boundary shares visual space with tuning. | Sprint 12 |

## Meetings

| Id | Finding | Impact | Frequency | Burden | Score | Evidence | Owning sprint |
| --- | --- | ---: | ---: | ---: | ---: | --- | --- |
| M01 | Catalog filtering, sorting, grouping, import intake, selected-row actions, and cleanup actions compete on one workbench. | 5 | 5 | 3 | 75 | Source: meeting filter controls, `control-addaudiofilesbutton`, `control-openmeetingdetailsbutton`, and cleanup controls. Hypothesis: next action is not visually dominant. | Sprint 6 / Sprint 8 |
| M02 | Safe cleanup appears as recommendation, review, and apply action with row/detail routes rather than one primary recommendation. | 5 | 4 | 3 | 60 | Source: `control-reviewmeetingcleanupsuggestionsbutton`, `control-applysafemeetingcleanupfixesbutton`, and contextual cleanup aliases. Fact: aliases need outcome parity. | Sprint 7 |
| M03 | Meeting detail combines reading, metadata repair, transcript recovery, speaker review, archive, and delete under maintenance. | 5 | 3 | 3 | 45 | Source: `control-retrytranscriptbutton`, `control-addspeakerlabelsbutton`, `control-archivebutton`, and `control-deletebutton`. Hypothesis: high-risk actions are hard to distinguish from repair work. | Sprint 9 / Sprint 13 |

## Cross-surface implications

- Preserve direct microphone, hosted-summary, profile-learning, reprocessing,
  archive, delete, and cleanup choices. None qualifies for silent automation.
- Consolidate duplicate aliases only after every route reports the same
  eligibility, preview, and result outcome.
- Move diagnostics and advanced tuning behind contextual recovery or Advanced;
  never remove them without a support route.
