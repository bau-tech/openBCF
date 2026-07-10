<script setup lang="ts">
import { onMounted, reactive, ref, watch } from 'vue'
import { callBinding } from '../bridge'
import MarkupEditor from './MarkupEditor.vue'
import type { ProjectExtensions, TopicDetail } from '../types'

const props = defineProps<{ topicGuid: string; extensions: ProjectExtensions | null }>()
const emit = defineEmits<{ back: []; statusChanged: [] }>()

const topic = ref<TopicDetail | null>(null)
const loading = ref(false)
const errorMessage = ref<string | null>(null)

const statusDraft = ref('')
const savingStatus = ref(false)

const newComment = ref('')
const postingComment = ref(false)

const viewpointSnapshots = reactive<Record<string, string>>({})
const attachingViewpoint = ref(false)
const applyingViewpoint = ref<string | null>(null)

async function load() {
  loading.value = true
  errorMessage.value = null
  try {
    const result = await callBinding<TopicDetail>('bcfIssueBinding', 'GetTopic', props.topicGuid)
    topic.value = result
    statusDraft.value = result.topicStatus ?? ''
    for (const viewpoint of result.viewpoints) {
      void loadSnapshot(viewpoint.guid)
    }
  } catch (err) {
    errorMessage.value = err instanceof Error ? err.message : String(err)
  } finally {
    loading.value = false
  }
}

async function loadSnapshot(viewpointGuid: string) {
  try {
    viewpointSnapshots[viewpointGuid] = await callBinding<string>(
      'bcfIssueBinding',
      'GetSnapshotDataUrl',
      props.topicGuid,
      viewpointGuid,
    )
  } catch {
    // Some viewpoints may not have a snapshot - leave the thumbnail blank.
  }
}

async function saveStatus() {
  savingStatus.value = true
  errorMessage.value = null
  try {
    await callBinding('bcfIssueBinding', 'UpdateTopicStatus', props.topicGuid, statusDraft.value)
    emit('statusChanged')
    await load()
  } catch (err) {
    errorMessage.value = err instanceof Error ? err.message : String(err)
  } finally {
    savingStatus.value = false
  }
}

async function postComment() {
  if (!newComment.value.trim()) return
  postingComment.value = true
  errorMessage.value = null
  try {
    await callBinding('bcfIssueBinding', 'CreateComment', props.topicGuid, newComment.value)
    newComment.value = ''
    await load()
  } catch (err) {
    errorMessage.value = err instanceof Error ? err.message : String(err)
  } finally {
    postingComment.value = false
  }
}

async function applyViewpoint(viewpointGuid: string) {
  applyingViewpoint.value = viewpointGuid
  errorMessage.value = null
  try {
    await callBinding('bcfIssueBinding', 'ApplyViewpoint', props.topicGuid, viewpointGuid)
  } catch (err) {
    errorMessage.value = err instanceof Error ? err.message : String(err)
  } finally {
    applyingViewpoint.value = null
  }
}

// Set while the markup editor is open for a freshly captured snapshot; null the rest of the time,
// which is also what hides the editor.
const markupSnapshotDataUrl = ref<string | null>(null)

async function attachViewpoint() {
  attachingViewpoint.value = true
  errorMessage.value = null
  try {
    // Capture only - CaptureCurrentViewpointSnapshot does not upload anything. The viewpoint
    // isn't saved to the server until the markup editor below confirms or is cancelled.
    markupSnapshotDataUrl.value = await callBinding<string>('bcfIssueBinding', 'CaptureCurrentViewpointSnapshot', props.topicGuid)
  } catch (err) {
    errorMessage.value = err instanceof Error ? err.message : String(err)
    attachingViewpoint.value = false
  }
}

async function saveMarkedUpSnapshot(dataUrl: string) {
  const base64 = dataUrl.replace(/^data:image\/png;base64,/, '')
  try {
    await callBinding('bcfIssueBinding', 'SaveViewpointSnapshot', props.topicGuid, base64)
    await load()
  } catch (err) {
    errorMessage.value = err instanceof Error ? err.message : String(err)
  } finally {
    markupSnapshotDataUrl.value = null
    attachingViewpoint.value = false
  }
}

function cancelMarkup() {
  markupSnapshotDataUrl.value = null
  attachingViewpoint.value = false
}

onMounted(load)
watch(() => props.topicGuid, load)
</script>

<template>
  <MarkupEditor
    v-if="markupSnapshotDataUrl"
    :image-data-url="markupSnapshotDataUrl"
    @save="saveMarkedUpSnapshot"
    @cancel="cancelMarkup"
  />
  <div v-else class="issue-detail">
    <button type="button" class="issue-detail__back" @click="emit('back')">&larr; Back</button>

    <p v-if="errorMessage" class="error">{{ errorMessage }}</p>
    <p v-if="loading">Loading…</p>

    <template v-if="topic">
      <h2>{{ topic.title }}</h2>
      <p class="issue-detail__meta">
        {{ topic.topicType ?? '—' }} · {{ topic.priority ?? '—' }} · assigned to {{ topic.assignedTo ?? '—' }}
        <span v-if="topic.dueDate"> · due {{ new Date(topic.dueDate).toLocaleDateString() }}</span>
      </p>
      <p v-if="topic.description">{{ topic.description }}</p>

      <label class="issue-detail__status">
        Status
        <select v-if="props.extensions?.topicStatuses.length" v-model="statusDraft">
          <option v-for="value in props.extensions.topicStatuses" :key="value" :value="value">{{ value }}</option>
        </select>
        <input v-else v-model="statusDraft" type="text" />
        <button type="button" :disabled="savingStatus || statusDraft === (topic.topicStatus ?? '')" @click="saveStatus">
          {{ savingStatus ? 'Saving…' : 'Save' }}
        </button>
      </label>

      <section class="issue-detail__viewpoints">
        <h3>Viewpoints</h3>
        <div class="issue-detail__viewpoint-list">
          <img
            v-for="viewpoint in topic.viewpoints"
            :key="viewpoint.guid"
            :src="viewpointSnapshots[viewpoint.guid]"
            :class="['issue-detail__viewpoint-thumb', { 'issue-detail__viewpoint-thumb--busy': applyingViewpoint === viewpoint.guid }]"
            title="Click to zoom to this viewpoint and select its parts"
            @click="applyViewpoint(viewpoint.guid)"
          />
        </div>
        <button type="button" :disabled="attachingViewpoint" @click="attachViewpoint">
          {{ attachingViewpoint ? 'Capturing…' : '+ Add viewpoint from current view' }}
        </button>
      </section>

      <section class="issue-detail__comments">
        <h3>Comments</h3>
        <div v-for="comment in topic.comments" :key="comment.guid" class="issue-detail__comment">
          <strong>{{ comment.author }}</strong>
          <span class="issue-detail__comment-date">{{ new Date(comment.date).toLocaleString() }}</span>
          <p>{{ comment.comment }}</p>
        </div>

        <form class="issue-detail__comment-form" @submit.prevent="postComment">
          <textarea v-model="newComment" rows="2" placeholder="Add a comment…"></textarea>
          <button type="submit" :disabled="postingComment">{{ postingComment ? 'Posting…' : 'Post' }}</button>
        </form>
      </section>
    </template>
  </div>
</template>
