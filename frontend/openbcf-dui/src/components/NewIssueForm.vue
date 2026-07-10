<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { callBinding } from '../bridge'
import MarkupEditor from './MarkupEditor.vue'
import type { ProjectExtensions } from '../types'

const props = defineProps<{ extensions: ProjectExtensions | null }>()
const emit = defineEmits<{ created: []; cancel: [] }>()

const title = ref('')
const topicType = ref('')
const topicStatus = ref('')
const priority = ref('')
const assignedTo = ref('')
const description = ref('')
const dueDate = ref('')
const attachCurrentView = ref(false)

const submitting = ref(false)
const errorMessage = ref<string | null>(null)

// Empty until GetHostName resolves (or if it fails) - this component's compiled frontend is
// shared verbatim between the Revit and Tekla clients, so the label can't hardcode either host's
// name; the template falls back to host-neutral wording while this is empty.
const hostName = ref('')

onMounted(async () => {
  try {
    hostName.value = await callBinding<string>('pingBinding', 'GetHostName')
  } catch {
    // Keep the host-neutral fallback.
  }
})

// Set while the markup editor is open for a just-created topic's snapshot; null the rest of the
// time, which is also what hides the editor.
const markupTopicGuid = ref<string | null>(null)
const markupSnapshotDataUrl = ref<string | null>(null)

async function submit() {
  if (!title.value.trim()) {
    errorMessage.value = 'Title is required.'
    return
  }

  submitting.value = true
  errorMessage.value = null

  try {
    const created = await callBinding<{ guid: string }>(
      'bcfIssueBinding',
      'CreateTopic',
      title.value,
      topicType.value || null,
      topicStatus.value || null,
      priority.value || null,
      description.value || null,
      assignedTo.value || null,
      dueDate.value ? new Date(dueDate.value).toISOString() : null,
    )

    if (attachCurrentView.value) {
      // Capture only - CaptureCurrentViewpointSnapshot does not upload anything. The viewpoint
      // isn't saved to the server until the markup editor below confirms or is cancelled.
      markupSnapshotDataUrl.value = await callBinding<string>('bcfIssueBinding', 'CaptureCurrentViewpointSnapshot', created.guid)
      markupTopicGuid.value = created.guid
      return
    }

    emit('created')
  } catch (err) {
    errorMessage.value = err instanceof Error ? err.message : String(err)
  } finally {
    submitting.value = false
  }
}

async function saveMarkedUpSnapshot(dataUrl: string) {
  const base64 = dataUrl.replace(/^data:image\/png;base64,/, '')
  try {
    await callBinding('bcfIssueBinding', 'SaveViewpointSnapshot', markupTopicGuid.value, base64)
  } catch (err) {
    errorMessage.value = err instanceof Error ? err.message : String(err)
  } finally {
    markupTopicGuid.value = null
    markupSnapshotDataUrl.value = null
    submitting.value = false
    emit('created')
  }
}

function cancelMarkup() {
  // The topic itself was already created; only the viewpoint attachment is abandoned.
  markupTopicGuid.value = null
  markupSnapshotDataUrl.value = null
  submitting.value = false
  emit('created')
}
</script>

<template>
  <MarkupEditor
    v-if="markupTopicGuid && markupSnapshotDataUrl"
    :image-data-url="markupSnapshotDataUrl"
    @save="saveMarkedUpSnapshot"
    @cancel="cancelMarkup"
  />
  <form v-else class="issue-form" @submit.prevent="submit">
    <h2>New Issue</h2>

    <label>
      Title
      <input v-model="title" type="text" required />
    </label>

    <label>
      Description
      <textarea v-model="description" rows="3"></textarea>
    </label>

    <label>
      Type
      <select v-if="props.extensions?.topicTypes.length" v-model="topicType">
        <option value="">—</option>
        <option v-for="value in props.extensions.topicTypes" :key="value" :value="value">{{ value }}</option>
      </select>
      <input v-else v-model="topicType" type="text" />
    </label>

    <label>
      Status
      <select v-if="props.extensions?.topicStatuses.length" v-model="topicStatus">
        <option value="">—</option>
        <option v-for="value in props.extensions.topicStatuses" :key="value" :value="value">{{ value }}</option>
      </select>
      <input v-else v-model="topicStatus" type="text" />
    </label>

    <label>
      Priority
      <select v-if="props.extensions?.priorities.length" v-model="priority">
        <option value="">—</option>
        <option v-for="value in props.extensions.priorities" :key="value" :value="value">{{ value }}</option>
      </select>
      <input v-else v-model="priority" type="text" />
    </label>

    <label>
      Assigned to
      <select v-if="props.extensions?.users.length" v-model="assignedTo">
        <option value="">—</option>
        <option v-for="value in props.extensions.users" :key="value" :value="value">{{ value }}</option>
      </select>
      <input v-else v-model="assignedTo" type="text" />
    </label>

    <label>
      Due date
      <input v-model="dueDate" type="date" />
    </label>

    <label class="issue-form__checkbox">
      <input v-model="attachCurrentView" type="checkbox" />
      Attach current{{ hostName ? ` ${hostName}` : '' }} view as a viewpoint
    </label>

    <p v-if="errorMessage" class="error">{{ errorMessage }}</p>

    <div class="issue-form__actions">
      <button type="submit" :disabled="submitting">{{ submitting ? 'Creating…' : 'Create' }}</button>
      <button type="button" @click="emit('cancel')">Cancel</button>
    </div>
  </form>
</template>
