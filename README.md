# VISOR
**Vocal Intelligent Synthetic Operating Runtime**

Smart Operating System for AOSP deployment.

## Voice (Teams-like capture)
- Android mic capture uses `AudioSource.VoiceCommunication` (fallback `VoiceRecognition`) plus AudioFx when available: Noise Suppression (NS), Automatic Gain Control (AGC), and Acoustic Echo Cancellation (AEC).
- Speech-to-text is powered by `Whisper.net` (`whisper-tiny.bin`) with streaming partial updates from `AndroidContinuousMicService`.
