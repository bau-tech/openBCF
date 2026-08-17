<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { callBinding, onBindingEvent } from '../bridge'
import bcfIcon from '../assets/bcf-icon.png'
import type { BcfProjectOption, ConnectResult, NeedsProjectPickResult } from '../types'

const emit = defineEmits<{ connected: [result: ConnectResult] }>()

const serverUrl = ref('')
const username = ref('')
const password = ref('')

const status = ref<'idle' | 'connecting' | 'error'>('idle')
const errorMessage = ref<string | null>(null)

// True until the silent auto-connect attempt (below) resolves one way or the other - the form
// stays hidden during this window so returning users see a brief "Reconnecting…" instead of a
// flash of the empty login form before it immediately gets replaced by the issue list.
const autoConnecting = ref(true)

const pendingProjects = ref<BcfProjectOption[] | null>(null)
const selectedProjectId = ref<string | null>(null)

// Two different hosts trigger the project picker two different ways (see bridge.ts's header
// comment and OpenBcf.ArchiCad29.Helper's BcfSessionBinding.CompleteConnect for why):
//   - 'push': Connect()'s own native call is still pending, and a projectPickRequested event
//     pushed it - answering goes back through the same pending call via ResolveProjectPick
//     (Rhino/Tekla/Revit, over WebView2).
//   - 'inline': Connect() already returned (with needsProjectPick in its result) - answering is a
//     brand new, independent CompleteConnect call (ArchiCAD).
const pickMode = ref<'push' | 'inline'>('push')

onMounted(async () => {
  try {
    const settings = await callBinding<{ serverUrl: string; username: string | null }>('bcfSessionBinding', 'GetSettings')
    serverUrl.value = settings.serverUrl
    username.value = settings.username ?? ''
  } catch {
    // Binding not available yet (e.g. running outside the WebView host) - leave fields blank.
  }

  onBindingEvent('bcfSessionBinding', 'projectPickRequested', (payload) => {
    const { projects, previousProjectId } = payload as { projects: BcfProjectOption[]; previousProjectId: string | null }
    pickMode.value = 'push'
    pendingProjects.value = projects
    selectedProjectId.value = previousProjectId ?? projects[0]?.id ?? null
  })

  try {
    const result = await callBinding<ConnectResult | null>('bcfSessionBinding', 'TryAutoConnect')
    if (result) {
      emit('connected', result)
      return
    }
  } catch {
    // No saved session, server unreachable, credentials rejected, etc. - fall through to the
    // normal Connect form below rather than surfacing an error for an attempt the user didn't
    // explicitly make.
  } finally {
    autoConnecting.value = false
  }
})

async function connect() {
  status.value = 'connecting'
  errorMessage.value = null

  try {
    const result = await callBinding<ConnectResult | NeedsProjectPickResult>(
      'bcfSessionBinding',
      'Connect',
      serverUrl.value,
      username.value || null,
      password.value || null,
    )
    status.value = 'idle'

    if ('needsProjectPick' in result) {
      pickMode.value = 'inline'
      pendingProjects.value = result.projects
      selectedProjectId.value = result.previousProjectId ?? result.projects[0]?.id ?? null
      return
    }

    emit('connected', result)
  } catch (err) {
    status.value = 'error'
    errorMessage.value = err instanceof Error ? err.message : String(err)
  }
}

async function confirmProjectPick() {
  const projectId = selectedProjectId.value
  pendingProjects.value = null
  try {
    if (pickMode.value === 'inline') {
      const result = await callBinding<ConnectResult>('bcfSessionBinding', 'CompleteConnect', projectId)
      emit('connected', result)
    } else {
      await callBinding('bcfSessionBinding', 'ResolveProjectPick', projectId)
    }
  } catch (err) {
    status.value = 'error'
    errorMessage.value = err instanceof Error ? err.message : String(err)
  }
}

async function cancelProjectPick() {
  pendingProjects.value = null
  try {
    if (pickMode.value === 'inline') {
      await callBinding('bcfSessionBinding', 'CompleteConnect', null)
    } else {
      await callBinding('bcfSessionBinding', 'ResolveProjectPick', null)
    }
  } catch (err) {
    status.value = 'error'
    errorMessage.value = err instanceof Error ? err.message : String(err)
  }
}
</script>

<template>
  <div class="shell">
    <header class="shell__header">
      <h1 class="shell__title">
        openBCF
        <img :src="bcfIcon" alt="" class="shell__logo" />
      </h1>
      <p>Connect to a BCF server</p>
    </header>
    <main class="shell__body">
      <p v-if="autoConnecting" class="status">Reconnecting…</p>
      <form v-else class="connect-form" @submit.prevent="connect">
        <label>
          Server URL
          <input v-model="serverUrl" type="text" placeholder="https://bcf.example.com" />
        </label>
        <label>
          Username
          <input v-model="username" type="text" autocomplete="username" />
        </label>
        <label>
          Password
          <input v-model="password" type="password" autocomplete="current-password" />
        </label>
        <button type="submit" :disabled="status === 'connecting'">
          {{ status === 'connecting' ? 'Connecting…' : 'Connect' }}
        </button>
      </form>

      <p v-if="errorMessage" class="error">{{ errorMessage }}</p>

      <div v-if="pendingProjects" class="project-picker">
        <p>Choose a BCF project:</p>
        <label v-for="project in pendingProjects" :key="project.id" class="project-picker__option">
          <input type="radio" :value="project.id" v-model="selectedProjectId" />
          {{ project.name ?? project.id }}
        </label>
        <div class="project-picker__actions">
          <button type="button" @click="confirmProjectPick">Select</button>
          <button type="button" @click="cancelProjectPick">Cancel</button>
        </div>
      </div>
    </main>
  </div>
</template>
