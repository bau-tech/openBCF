<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { callBinding } from '../bridge'
import bcfIcon from '../assets/bcf-icon.png'
import IssueDetail from './IssueDetail.vue'
import IssueList from './IssueList.vue'
import NewIssueForm from './NewIssueForm.vue'
import type { ArchiveFileResult, ConnectResult, ProjectExtensions, TopicListItem } from '../types'

const props = defineProps<{ connection: ConnectResult }>()
const emit = defineEmits<{ disconnected: [] }>()

const topics = ref<TopicListItem[]>([])
const extensions = ref<ProjectExtensions | null>(null)
const selectedTopicGuid = ref<string | null>(null)
const creatingNew = ref(false)
const loading = ref(false)
const exporting = ref(false)
const importing = ref(false)
const disconnecting = ref(false)
const errorMessage = ref<string | null>(null)
const statusMessage = ref<string | null>(null)

async function loadTopics() {
  loading.value = true
  errorMessage.value = null
  try {
    topics.value = await callBinding<TopicListItem[]>('bcfIssueBinding', 'ListTopics')
  } catch (err) {
    errorMessage.value = err instanceof Error ? err.message : String(err)
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  try {
    extensions.value = await callBinding<ProjectExtensions>('bcfIssueBinding', 'GetExtensions')
  } catch (err) {
    errorMessage.value = err instanceof Error ? err.message : String(err)
  }
  await loadTopics()
})

function selectTopic(guid: string) {
  creatingNew.value = false
  selectedTopicGuid.value = guid
}

function startNewIssue() {
  selectedTopicGuid.value = null
  creatingNew.value = true
}

function backToList() {
  selectedTopicGuid.value = null
  creatingNew.value = false
}

async function onTopicCreated() {
  creatingNew.value = false
  await loadTopics()
}

async function exportZip() {
  exporting.value = true
  errorMessage.value = null
  statusMessage.value = null
  try {
    const result = await callBinding<ArchiveFileResult | null>('bcfArchiveBinding', 'ExportToFile')
    if (result) {
      statusMessage.value = `Exported ${result.topicCount} issue(s) to ${result.path}`
    }
  } catch (err) {
    errorMessage.value = err instanceof Error ? err.message : String(err)
  } finally {
    exporting.value = false
  }
}

async function disconnect() {
  disconnecting.value = true
  try {
    await callBinding('bcfSessionBinding', 'Disconnect')
  } finally {
    // Nothing left to do with this session either way - go back to the Connect form even if the
    // call itself failed, rather than trap the user on a panel with no way out.
    emit('disconnected')
  }
}

async function importZip() {
  importing.value = true
  errorMessage.value = null
  statusMessage.value = null
  try {
    const result = await callBinding<ArchiveFileResult | null>('bcfArchiveBinding', 'ImportFromFile')
    if (result) {
      statusMessage.value = `Imported ${result.topicCount} issue(s) from ${result.path}`
      await loadTopics()
    }
  } catch (err) {
    errorMessage.value = err instanceof Error ? err.message : String(err)
  } finally {
    importing.value = false
  }
}
</script>

<template>
  <div class="shell">
    <header class="shell__header">
      <div class="shell__header-row">
        <h1 class="shell__title">
          openBCF
          <img :src="bcfIcon" alt="" class="shell__logo" />
        </h1>
        <button type="button" class="shell__disconnect" :disabled="disconnecting" @click="disconnect">
          {{ disconnecting ? 'Disconnecting…' : 'Disconnect' }}
        </button>
      </div>
      <p>{{ props.connection.projectName ?? props.connection.projectId }} on {{ props.connection.serverUrl }}</p>
    </header>
    <main class="shell__body">
      <p v-if="errorMessage" class="error">{{ errorMessage }}</p>
      <p v-else-if="statusMessage" class="status">{{ statusMessage }}</p>

      <IssueList
        v-if="!selectedTopicGuid && !creatingNew"
        :topics="topics"
        :loading="loading"
        :exporting="exporting"
        :importing="importing"
        @select="selectTopic"
        @new-issue="startNewIssue"
        @refresh="loadTopics"
        @export-zip="exportZip"
        @import-zip="importZip"
      />
      <NewIssueForm
        v-else-if="creatingNew"
        :extensions="extensions"
        @created="onTopicCreated"
        @cancel="backToList"
      />
      <IssueDetail
        v-else-if="selectedTopicGuid"
        :topic-guid="selectedTopicGuid"
        :extensions="extensions"
        @back="backToList"
        @status-changed="loadTopics"
      />
    </main>
  </div>
</template>
