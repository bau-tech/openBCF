<script setup lang="ts">
import type { TopicListItem } from '../types'

defineProps<{ topics: TopicListItem[]; loading: boolean; exporting: boolean; importing: boolean }>()
const emit = defineEmits<{ select: [guid: string]; newIssue: []; refresh: []; exportZip: []; importZip: [] }>()

function formatDate(value: string | null): string {
  return value ? new Date(value).toLocaleDateString() : '—'
}
</script>

<template>
  <div class="issue-list">
    <div class="issue-list__toolbar">
      <button type="button" @click="emit('newIssue')">+ New Issue</button>
      <button type="button" :disabled="loading" @click="emit('refresh')">
        {{ loading ? 'Refreshing…' : 'Refresh' }}
      </button>
      <button type="button" :disabled="exporting" @click="emit('exportZip')">
        {{ exporting ? 'Exporting…' : 'Export .bcfzip' }}
      </button>
      <button type="button" :disabled="importing" @click="emit('importZip')">
        {{ importing ? 'Importing…' : 'Import .bcfzip' }}
      </button>
    </div>

    <p v-if="!loading && topics.length === 0" class="issue-list__empty">No issues yet.</p>

    <table v-else class="issue-list__table">
      <thead>
        <tr>
          <th>Title</th>
          <th>Status</th>
          <th>Type</th>
          <th>Priority</th>
          <th>Assigned to</th>
          <th>Due</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="topic in topics" :key="topic.guid" class="issue-list__row" @click="emit('select', topic.guid)">
          <td>{{ topic.title }}</td>
          <td>{{ topic.topicStatus ?? '—' }}</td>
          <td>{{ topic.topicType ?? '—' }}</td>
          <td>{{ topic.priority ?? '—' }}</td>
          <td>{{ topic.assignedTo ?? '—' }}</td>
          <td>{{ formatDate(topic.dueDate) }}</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
