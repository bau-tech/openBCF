export interface BcfProjectOption {
  id: string
  name: string | null
}

export interface ConnectResult {
  serverUrl: string
  projectId: string
  projectName: string | null
}

export interface TopicListItem {
  guid: string
  title: string
  topicType: string | null
  topicStatus: string | null
  priority: string | null
  assignedTo: string | null
  creationDate: string | null
  dueDate: string | null
}

export interface CommentItem {
  guid: string
  date: string
  author: string
  comment: string
}

export interface ViewpointRef {
  guid: string
}

export interface TopicDetail extends TopicListItem {
  description: string | null
  creationAuthor: string | null
  comments: CommentItem[]
  viewpoints: ViewpointRef[]
}

export interface ProjectExtensions {
  topicTypes: string[]
  topicStatuses: string[]
  priorities: string[]
  users: string[]
  stages: string[]
}

export interface ArchiveFileResult {
  path: string
  topicCount: number
}
