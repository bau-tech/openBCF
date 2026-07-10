<script setup lang="ts">
import { ref } from 'vue'
import ConnectForm from './components/ConnectForm.vue'
import IssuesView from './components/IssuesView.vue'
import type { ConnectResult } from './types'

const connection = ref<ConnectResult | null>(null)
</script>

<template>
  <ConnectForm v-if="!connection" @connected="(result) => (connection = result)" />
  <IssuesView v-else :connection="connection" @disconnected="connection = null" />
</template>
